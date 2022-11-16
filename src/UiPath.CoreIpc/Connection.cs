namespace UiPath.CoreIpc;
using static TaskCompletionPool<Response>;
using static IOHelpers;
using Microsoft.IO;
using Newtonsoft.Json;
using System.Buffers;
public sealed class Connection : IDisposable, IArrayPool<char>
{
    static readonly JsonSerializer ObjectArgsSerializer = new() { DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate, NullValueHandling = NullValueHandling.Ignore };
    private static readonly IOException ClosedException = new("Connection closed.");
    private readonly ConcurrentDictionary<int, ManualResetValueTaskSource> _requests = new();
    private int _requestCounter = -1;
    private readonly int _maxMessageSize;
    private readonly Lazy<Task> _receiveLoop;
    private readonly SemaphoreSlim _sendLock = new(1);
    private readonly WaitCallback _onResponse;
    private readonly WaitCallback _onRequest;
    private readonly WaitCallback _onCancellation;
    private readonly Action<object> _cancelRequest;
    private readonly Action<object> _cancelUploadRequest;
    private readonly byte[] _buffer = new byte[sizeof(long)];
    private readonly NestedStream _nestedStream;
    public Connection(Stream network, ILogger logger, string name, int maxMessageSize = int.MaxValue)
    {
        Network = network;
        _nestedStream = new NestedStream(network, 0);
        Logger = logger;
        Name = $"{name} {GetHashCode()}";
        _maxMessageSize = maxMessageSize;
        _receiveLoop = new(ReceiveLoop);
        _onResponse = response => OnResponseReceived((Response)response);
        _onRequest = request => OnRequestReceived((Request)request);
        _onCancellation = requestId => OnCancellationReceived((int)requestId);
        _cancelRequest = requestId => CancelRequest((int)requestId);
        _cancelUploadRequest = requestId => CancelUploadRequest((int)requestId);
    }
    internal Server Server { get; private set; }
    public Stream Network { get; }
    public ILogger Logger { get; internal set; }
    public bool LogEnabled => Logger.Enabled();
    public string Name { get; }
    public override string ToString() => Name;
    public int NewRequestId() => Interlocked.Increment(ref _requestCounter);
    public Task Listen() => _receiveLoop.Value;
    public event EventHandler<EventArgs> Closed;
    public void SetServer(ListenerSettings settings, IClient client = null) => Server = new(settings, this, client);
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    internal async ValueTask<Response> RemoteCall(Request request, CancellationToken token)
    {
        var requestCompletion = Rent();
        var requestId = request.Id;
        _requests[requestId] = requestCompletion;
        var cancelRequest = request.UploadStream == null ? _cancelRequest : _cancelUploadRequest;
        CancellationTokenRegistration tokenRegistration = default;
        try
        {
            await Send(request, token);
            token.UnsafeRegister(cancelRequest, requestId);
            return await requestCompletion.ValueTask();
        }
        finally
        {
            _requests.TryRemove(requestId, out _);
            tokenRegistration.Dispose();
            requestCompletion.Return();
        }
    }
    internal ValueTask Send(Request request, CancellationToken token)
    {
        var uploadStream = request.UploadStream;
        var requestBytes = Serialize(new[] { request }.Concat(request.Parameters));
        return uploadStream == null ?
            SendMessage(MessageType.Request, requestBytes, token) :
            SendStream(MessageType.UploadRequest, requestBytes, uploadStream, token);
    }
    void CancelRequest(int requestId)
    {
        CancelServerCall(requestId).LogException(Logger, this);
        TryCancelRequest(requestId);
        return;
        Task CancelServerCall(int requestId) =>
            SendMessage(MessageType.CancellationRequest, Serialize(new[] { new CancellationRequest(requestId) }), default).AsTask();
    }
    void CancelUploadRequest(int requestId)
    {
        Dispose();
        TryCancelRequest(requestId);
    }
    private void TryCancelRequest(int requestId)
    {
        if (_requests.TryRemove(requestId, out var requestCompletion))
        {
            requestCompletion.SetCanceled();
        }
    }
    internal ValueTask Send(Response response, CancellationToken cancellationToken)
    {
        var responseBytes = Serialize(new[] { response, response.Data });
        return response.DownloadStream == null ?
            SendMessage(MessageType.Response, responseBytes, cancellationToken) :
            SendDownloadStream(responseBytes, response.DownloadStream, cancellationToken);
    }
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask SendDownloadStream(RecyclableMemoryStream responseBytes, Stream downloadStream, CancellationToken cancellationToken)
    {
        using (downloadStream)
        {
            await SendStream(MessageType.DownloadResponse, responseBytes, downloadStream, cancellationToken);
        }
    }
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask SendStream(MessageType messageType, RecyclableMemoryStream data, Stream userStream, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        CancellationTokenRegistration tokenRegistration = default;
        try
        {
            tokenRegistration = cancellationToken.UnsafeRegister(state => ((Connection)state).Dispose(), this);
            await Network.WriteMessage(messageType, data, cancellationToken);
            await Network.WriteBuffer(BitConverter.GetBytes(userStream.Length), cancellationToken);
            const int DefaultCopyBufferSize = 81920;
            await userStream.CopyToAsync(Network, DefaultCopyBufferSize, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
            tokenRegistration.Dispose();
        }
    }
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask SendMessage(MessageType messageType, RecyclableMemoryStream data, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await Network.WriteMessage(messageType, data, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }
    public void Dispose()
    {
        var closedHandler = Closed;
        if (closedHandler == null || (closedHandler = Interlocked.CompareExchange(ref Closed, null, closedHandler)) == null)
        {
            return;
        }
        _sendLock.AssertDisposed();
        Network.Dispose();
        Server.CancelRequests();
        try
        {
            closedHandler.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, this);
        }
        CompleteRequests();
    }
    private void CompleteRequests()
    {
        foreach (var requestId in _requests.Keys)
        {
            if (_requests.TryRemove(requestId, out var requestCompletion))
            {
                requestCompletion.SetException(ClosedException);
            }
        }
    }
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    private async ValueTask<bool> ReadBuffer(int length)
    {
        int offset = 0;
        int toRead = length;
        do
        {
            var read = await Network.ReadAsync(
#if NET461
                _buffer, offset, toRead);
#else
                _buffer.AsMemory(offset, toRead));
#endif
            if (read == 0)
            {
                return false;
            }
            offset += read;
            toRead -= read;
        }
        while (toRead > 0);
        return true;
    }
    private async Task ReceiveLoop()
    {
        try
        {
            while (await ReadBuffer(HeaderLength))
            {
                Debug.Assert(SynchronizationContext.Current == null);
                var length = BitConverter.ToInt32(_buffer, startIndex: 1);
                if (length > _maxMessageSize)
                {
                    throw new InvalidDataException($"Message too large. The maximum message size is {_maxMessageSize / (1024 * 1024)} megabytes.");
                }
                _nestedStream.Reset(length);
                await HandleMessage();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"{nameof(ReceiveLoop)} {Name}");
        }
        if (LogEnabled)
        {
            Log($"{nameof(ReceiveLoop)} {Name} finished.");
        }
        Dispose();
        return;
#if !NET461
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
        async ValueTask HandleMessage()
        {
            var messageType = (MessageType)_buffer[0];
            switch (messageType)
            {
                case MessageType.Response:
                    RunAsync(_onResponse, await Deserialize<Response>());
                    break;
                case MessageType.Request:
                    RunAsync(_onRequest, await Deserialize<Request>());
                    break;
                case MessageType.CancellationRequest:
                    RunAsync(_onCancellation, (await Deserialize<CancellationRequest>()).RequestId);
                    break;
                case MessageType.UploadRequest:
                    await OnUploadRequest();
                    return;
                case MessageType.DownloadResponse:
                    await OnDownloadResponse();
                    return;
                default:
                    if (LogEnabled)
                    {
                        Log("Unknown message type " + messageType);
                    }
                    break;
            };
        }
    }
    static void RunAsync(WaitCallback callback, object state) => ThreadPool.UnsafeQueueUserWorkItem(callback, state);
    private async Task OnDownloadResponse()
    {
        var response = await Deserialize<Response>();
        await EnterStreamMode();
        var streamDisposed = new TaskCompletionSource<bool>();
        EventHandler disposedHandler = delegate { streamDisposed.TrySetResult(true); };
        _nestedStream.Disposed += disposedHandler;
        try
        {
            response.DownloadStream = _nestedStream;
            OnResponseReceived(response);
            await streamDisposed.Task;
        }
        finally
        {
            _nestedStream.Disposed -= disposedHandler;
        }
    }
    private async Task OnUploadRequest()
    {
        var request = await Deserialize<Request>();
        await EnterStreamMode();
        using (_nestedStream)
        {
            request.UploadStream = _nestedStream;
            await OnRequestReceived(request);
        }
    }
    private async Task EnterStreamMode()
    {
        if (!await ReadBuffer(sizeof(long)))
        {
            throw ClosedException;
        }
        var userStreamLength = BitConverter.ToInt64(_buffer, startIndex: 0);
        _nestedStream.Reset(userStreamLength);
    }
    private RecyclableMemoryStream Serialize(IEnumerable<object> values)
    {
        var stream = GetStream();
        try
        {
            stream.Position = HeaderLength;
            using var writer = new JsonTextWriter(new StreamWriter(stream)) { ArrayPool = this, CloseOutput = false };
            foreach (var value in values)
            {
                ObjectArgsSerializer.Serialize(writer, value);
            }
            writer.Flush();
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    private async ValueTask<T> Deserialize<T>()
    {
        using var stream = GetStream((int)_nestedStream.Length);
        await _nestedStream.CopyToAsync(stream);
        stream.Position = 0;
        using var reader = new JsonTextReader(new StreamReader(stream)) { ArrayPool = this, SupportMultipleContent = true };
        return ObjectArgsSerializer.Deserialize<T>(reader);
    }
    private void OnCancellationReceived(int requestId)
    {
        try
        {
            Server.CancelRequest(requestId);
        }
        catch(Exception ex)
        {
            Log(ex);
        }
    }
    private void Log(Exception ex) => Logger.LogException(ex, Name);
    private ValueTask OnRequestReceived(Request request)
    {
        try
        {
            return Server.OnRequestReceived(request);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
        return default;
    }
    private void OnResponseReceived(Response response)
    {
        try
        {
            if (LogEnabled)
            {
                Log($"Received response for request {response.RequestId} {Name}.");
            }
            if (_requests.TryRemove(response.RequestId, out var completionSource))
            {
                completionSource.SetResult(response);
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }
    public void Log(string message) => Logger.LogInformation(message);
    char[] IArrayPool<char>.Rent(int minimumLength) => ArrayPool<char>.Shared.Rent(minimumLength);
    void IArrayPool<char>.Return(char[] array) => ArrayPool<char>.Shared.Return(array);
}