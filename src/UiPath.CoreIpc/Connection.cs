using MessagePack;
using Microsoft.IO;
using System.Buffers;
namespace UiPath.CoreIpc;
using static TaskCompletionPool<Response>;
using static IOHelpers;
public sealed class Connection : IDisposable
{
    static readonly MessagePackSerializerOptions Contractless = MessagePack.Resolvers.ContractlessStandardResolver.Options;
    static readonly ConcurrentDictionary<(Type, string), Method> Methods = new();
    private static readonly IOException ClosedException = new("Connection closed.");
    private readonly ConcurrentDictionary<int, OutgoingRequest> _requests = new();
    private int _requestCounter = -1;
    private readonly int _maxMessageSize;
    private readonly Lazy<Task> _receiveLoop;
    private readonly SemaphoreSlim _sendLock = new(1);
    private readonly WaitCallback _onResponse;
    private readonly WaitCallback _onRequest;
    private readonly WaitCallback _onCancellation;
    private readonly Action<object> _cancelRequest;
    private readonly byte[] _buffer = new byte[sizeof(long)];
    private readonly NestedStream _nestedStream;
    private readonly MessagePackStreamReader _streamReader;
    public Connection(Stream network, ILogger logger, string name, int maxMessageSize = int.MaxValue)
    {
        Network = network;
        _nestedStream = new NestedStream(network, 0);
        _streamReader = new(_nestedStream);
        Logger = logger;
        Name = $"{name} {GetHashCode()}";
        _maxMessageSize = maxMessageSize;
        _receiveLoop = new(ReceiveLoop);
        _onResponse = response => OnResponseReceived((IncomingResponse)response);
        _onRequest = request => OnRequestReceived((IncomingRequest)request);
        _onCancellation = requestId => OnCancellationReceived((int)requestId);
        _cancelRequest = requestId => CancelRequest((int)requestId);
    }
    internal Server Server { get; private set; }
    public Stream Network { get; }
    public ILogger Logger { get; internal set; }
    public bool LogEnabled => Logger.Enabled();
    public string Name { get; }
    public override string ToString() => Name;
    public int NewRequestId() => Interlocked.Increment(ref _requestCounter);
    public Task Listen() => _receiveLoop.Value;
    public event EventHandler<EventArgs> Closed = delegate{};
    public void SetServer(ListenerSettings settings, IClient client = null) => Server = new(settings, this, client);
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    internal async ValueTask<Response> RemoteCall(Request request, CancellationToken token)
    {
        await Send(request, token);
        var requestCompletion = Rent();
        var requestId = request.Id;
        _requests[requestId] = new(requestCompletion, request.ResponseType);
        var tokenRegistration = token.UnsafeRegister(_cancelRequest, requestId);
        try
        {
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
        if (request.Parameters is [Stream uploadStream, ..])
        {
            request.Parameters[0] = null;
        }
        else
        {
            uploadStream = null;
        }
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
    ManualResetValueTaskSource Completion(int requestId) => _requests.TryRemove(requestId, out var outgoingRequest) ? outgoingRequest.Completion : null;
    private void TryCancelRequest(int requestId) => Completion(requestId)?.SetCanceled();
    internal ValueTask Send(Response response, CancellationToken cancellationToken)
    {
        var downloadStream = response.Data as Stream;
        var responseBytes = Serialize(new[] { response, downloadStream == null ? response.Data : null});
        return downloadStream == null ?
            SendMessage(MessageType.Response, responseBytes, cancellationToken) :
            SendDownloadStream(responseBytes, downloadStream, cancellationToken);
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
        _streamReader.Dispose();
        Server?.CancelRequests();
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
            Completion(requestId)?.SetException(ClosedException);
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
                _streamReader.DiscardBufferedData();
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
                    var response = await DeserializeResponse();
                    if (response != null)
                    {
                        RunAsync(_onResponse, response);
                    }
                    break;
                case MessageType.Request:
                    RunAsync(_onRequest, await DeserializeRequest());
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
        var response = await DeserializeResponse();
        if (response == null)
        {
            return;
        }
        await EnterStreamMode();
        var streamDisposed = new TaskCompletionSource<bool>();
        EventHandler disposedHandler = delegate { streamDisposed.TrySetResult(true); };
        _nestedStream.Disposed += disposedHandler;
        try
        {
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
        var request = await DeserializeRequest();
        await EnterStreamMode();
        using (_nestedStream)
        {
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
        _streamReader.DiscardBufferedData();
    }
    private RecyclableMemoryStream Serialize(IEnumerable<object> values)
    {
        var stream = GetStream();
        try
        {
            stream.Position = HeaderLength;
            foreach (var value in values)
            {
                MessagePackSerializer.Serialize(value?.GetType() ?? typeof(object), (IBufferWriter<byte>)stream, value, Contractless);
            }
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
    private async ValueTask<T> Deserialize<T>() => MessagePackSerializer.Deserialize<T>(await ReadBytes(), Contractless);
    private async ValueTask<ReadOnlySequence<byte>> ReadBytes() => await _streamReader.ReadAsync(default) ?? throw ClosedException;
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
    private async ValueTask<IncomingResponse> DeserializeResponse()
    {
        var response = await Deserialize<Response>();
        if (LogEnabled)
        {
            Log($"Received response for request {response.RequestId} {Name}.");
        }
        IncomingResponse result;
        if (!_requests.TryRemove(response.RequestId, out var outgoingRequest))
        {
            return null;
        }
        result = new(response, outgoingRequest.Completion);
        var bytes = await ReadBytes();
        var responseType = outgoingRequest.ResponseType;
        if (response.Error == null)
        {
            response.Data = responseType == typeof(Stream) ? _nestedStream : MessagePackSerializer.Deserialize(responseType, bytes, Contractless);
        }
        return result;
    }
    private async ValueTask<IncomingRequest> DeserializeRequest()
    {
        var request = await Deserialize<Request>();
        if (LogEnabled)
        {
            Log($"{Name} received request {request}");
        }
        if (!Server.Endpoints.TryGetValue(request.Endpoint, out var endpoint))
        {
            await OnError(request, new ArgumentOutOfRangeException(nameof(request.Endpoint), $"{Name} cannot find endpoint {request.Endpoint}"));
        }
        var method = GetMethod(endpoint.Contract, request.MethodName);
        var args = new object[method.Parameters.Length];
        for (int index = 0; index < args.Length; index++)
        {
            var type = method.Parameters[index].ParameterType;
            var bytes = await ReadBytes();
            if (type == typeof(CancellationToken))
            {
                continue;
            }
            else if (type == typeof(Stream))
            {
                args[index] = _nestedStream;
                continue;
            }
            var arg = MessagePackSerializer.Deserialize(type, bytes, Contractless);
            args[index] = CheckMessage(arg, type);
        }
        request.Parameters = args;
        return new(request, method, endpoint);
        object CheckMessage(object argument, Type parameterType)
        {
            if (parameterType == typeof(Message) && argument == null)
            {
                argument = new Message();
            }
            if (argument is Message message)
            {
                message.CallbackContract = endpoint.CallbackContract;
                message.Client = Server.Client;
            }
            return argument;
        }
    }
    private ValueTask OnRequestReceived(IncomingRequest request)
    {
        try
        {
            return Server.OnRequestReceived(request);
        }
        catch (Exception ex)
        {
            Log(ex);
            return default;
        }
    }
    internal ValueTask OnError(Request request, Exception ex)
    {
        Logger.LogException(ex, $"{Name} {request}");
        return Send(Response.Fail(request, ex), default);
    }
    private void OnResponseReceived(IncomingResponse incomingResponse)
    {
        try
        {
            incomingResponse.Completion.SetResult(incomingResponse.Response);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }
    public void Log(string message) => Logger.LogInformation(message);
    static Method GetMethod(Type contract, string methodName) => Methods.GetOrAdd((contract, methodName),
        ((Type contract, string methodName) key) => new(key.contract.GetInterfaceMethod(key.methodName)));
}
record IncomingRequest(Request Request, Method Method, EndpointSettings Endpoint);
readonly record struct OutgoingRequest(ManualResetValueTaskSource Completion, Type ResponseType);
record IncomingResponse(Response Response, ManualResetValueTaskSource Completion);