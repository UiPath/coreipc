using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;

namespace UiPath.Ipc;

using static TaskCompletionPool<Response>;
using static IOHelpers;

internal sealed class Connection : IDisposable
{
    private static readonly IOException ClosedException = new("Connection closed.");
    private readonly ConcurrentDictionary<string, ManualResetValueTaskSource> _requests = new();
    private long _requestCounter = -1;
    private readonly int _maxMessageSize;
    private readonly Lazy<Task> _receiveLoop;
    private readonly SemaphoreSlim _sendLock = new(1);
    private readonly WaitCallback _onResponse;
    private readonly WaitCallback _onRequest;
    private readonly WaitCallback _onCancellation;
    private readonly Action<object?> _cancelRequest;
    private readonly byte[] _buffer = new byte[sizeof(long)];
    private readonly NestedStream _nestedStream;

    public string DebugName { get; }
    public ILogger? Logger { get; }

    public Stream Network { get; }

    [MemberNotNullWhen(returnValue: true, nameof(Logger))]
    public bool LogEnabled => Logger.Enabled();

    public Connection(Stream network, string debugName, ILogger? logger, int maxMessageSize = int.MaxValue)
    {
        Network = network;
        _nestedStream = new NestedStream(network, 0);
        DebugName = debugName;
        Logger = logger;
        _maxMessageSize = maxMessageSize;
        _onResponse = response => OnResponseReceived((Response)response!);
        _onRequest = request => OnRequestReceived((Request)request!);
        _onCancellation = requestId => OnCancellationReceived((CancellationRequest)requestId!);
        _cancelRequest = requestId => CancelRequest((string)requestId!);
        _receiveLoop = new(ReceiveLoop);
    }

    public override string ToString() => DebugName;
    public string NewRequestId() => Interlocked.Increment(ref _requestCounter).ToString();
    public Task Listen() => _receiveLoop.Value;

    public event Func<Request, ValueTask>? RequestReceived;
    public event Action<string>? CancellationReceived;
    public event EventHandler<EventArgs>? Closed;
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<Response> RemoteCall(Request request, CancellationToken token)
    {
        var requestCompletion = Rent();
        var requestId = request.Id;
        _requests[requestId] = requestCompletion;
        var tokenRegistration = token.UnsafeRegister(_cancelRequest, requestId);
        try
        {
            Logger?.LogInformation("Sending the request");
            await Send(request, token);
            Logger?.LogInformation("Sent the request");
        }
        catch (Exception ex)
        {
            Logger?.LogTrace($"Caught exception while sending the request. Ex: {ex}");
            tokenRegistration.Dispose();
            if (_requests.TryRemove(requestId, out _))
            {
                requestCompletion.Return();
            }
            throw;
        }
        try
        {
            Logger?.LogInformation("Waiting for the completion source to complete.");
            Response response;
            try
            {
                response = await requestCompletion.ValueTask();
                Logger?.LogInformation("The completion source completed successfully.");
            }
            catch (Exception ex)
            {
                Logger?.LogInformation($"The completion source failed. Ex: {ex}");
                throw;
            }
            return response;
        }
        finally
        {
            _requests.TryRemove(requestId, out _);
            tokenRegistration.Dispose();
            requestCompletion.Return();
        }
    }
    public ValueTask Send(Request request, CancellationToken token)
    {
        Logger?.LogInformation("Connection.Send...");
        var uploadStream = request.UploadStream;
        var requestBytes = SerializeToStream(request);
        return uploadStream == null ?
            SendMessage(MessageType.Request, requestBytes, token) :
            SendStream(MessageType.UploadRequest, requestBytes, uploadStream, token);
    }
    private void CancelRequest(string requestId)
    {
        CancelServerCall(requestId).LogException(Logger, this);
        if (_requests.TryRemove(requestId, out var requestCompletion))
        {
            requestCompletion.SetCanceled();
        }
        return;
        Task CancelServerCall(string requestId) =>
            SendMessage(MessageType.CancellationRequest, SerializeToStream(new CancellationRequest(requestId)), default).AsTask();
    }
    public ValueTask Send(Response response, CancellationToken cancellationToken)
    {
        var responseBytes = SerializeToStream(response);
        return response.DownloadStream == null ?
            SendMessage(MessageType.Response, responseBytes, cancellationToken) :
            SendDownloadStream(responseBytes, response.DownloadStream, cancellationToken);
    }
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask SendDownloadStream(MemoryStream responseBytes, Stream downloadStream, CancellationToken cancellationToken)
    {
        using (downloadStream)
        {
            await SendStream(MessageType.DownloadResponse, responseBytes, downloadStream, cancellationToken);
        }
    }
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask SendStream(MessageType messageType, Stream data, Stream userStream, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        CancellationTokenRegistration tokenRegistration = default;
        try
        {
            tokenRegistration = cancellationToken.UnsafeRegister(state => ((Connection)state!).Dispose(), this);
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
    private async ValueTask SendMessage(MessageType messageType, MemoryStream data, CancellationToken cancellationToken)
    {
        Logger?.LogInformation("Connection.SendMessage: Awaiting the acquiring of the sendLock");
        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            Logger?.LogInformation($"Connection.SendMessage: sendLock was successfully aquired. Pushing the bytes onto the network. ByteCount: {data.Length}");
            await Network.WriteMessage(messageType, data, CancellationToken.None);
            Logger?.LogInformation("Connection.SendMessage: Successfully pushed the bytes.");
        }
        finally
        {
            Logger?.LogInformation("Connection.SendMessage: Releasing the sendLock.");
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
            int read;
            try
            {
                read = await Network.ReadAsync(
#if NET461
                    _buffer, offset, toRead);
#else
                    _buffer.AsMemory(offset, toRead));
#endif
            }
            catch (OperationCanceledException ex) when (Network is PipeStream)
            {
                // Originally we decided to throw this exception the 2nd time we caught it, but later it was discovered that the NodeJS runtime continuosly retries.

                // In some Windows client environments, OperationCanceledException is sporadically thrown on named pipe ReadAsync operation (ERROR_OPERATION_ABORTED on overlapped ReadFile)
                // The cause has not yet been discovered(os specific, antiviruses, monitoring application), and we have implemented a retry system
                // ROBO-3083

                Logger.LogException(ex, $"Retrying ReadAsync for {Network.GetType()}");
                await Task.Delay(10); //Without this delay, on net framework can get OperationCanceledException on the second ReadAsync call
                continue;
            }

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
                var length = BitConverter.ToInt32(_buffer, startIndex: 1);

                Debug.Assert(SynchronizationContext.Current is null);
                if (length > _maxMessageSize)
                {
                    throw new InvalidDataException($"Message too large. The maximum message size is {_maxMessageSize / (1024 * 1024)} megabytes.");
                }
                _nestedStream.Reset(length);
                await HandleMessage();
            }
            Logger?.Connection_ReceiveLoopEndedSuccessfully(DebugName);
        }
        catch (Exception ex)
        {
            Logger?.Connection_ReceiveLoopFailed(DebugName, ex);
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
                    RunAsync(_onCancellation, await Deserialize<CancellationRequest>());
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
    static void RunAsync(WaitCallback callback, object? state) => ThreadPool.UnsafeQueueUserWorkItem(callback, state);
    private async Task OnDownloadResponse()
    {
        var response = (await Deserialize<Response>())!;
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
        var request = (await Deserialize<Request>())!;
        await EnterStreamMode();
        using (_nestedStream)
        {
            request.UploadStream = _nestedStream;
            await OnRequestReceivedAsyncSafe(request);
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
    private MemoryStream SerializeToStream(object value)
    {
        var stream = GetStream();
        try
        {
            stream.Position = HeaderLength;
            IpcJsonSerializer.Instance.Serialize(value, stream);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
    private ValueTask<T?> Deserialize<T>() => IpcJsonSerializer.Instance.DeserializeAsync<T>(_nestedStream, Logger);

    private void OnCancellationReceived(CancellationRequest cancellationRequest)
    {
        try
        {
            CancellationReceived?.Invoke(cancellationRequest.RequestId);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }
    private void OnRequestReceived(Request? request)
    {
        _ = OnRequestReceivedAsyncSafe(request);
    }

    private async Task OnRequestReceivedAsyncSafe(Request request)
    {
        try
        {
            await (RequestReceived?.Invoke(request) ?? default);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }
    private void OnResponseReceived(Response response)
    {
        try
        {
            if (LogEnabled)
            {
                Log($"Received response for request {response.RequestId} {DebugName}.");
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

    private void Log(Exception ex) => Logger?.LogException(ex, DebugName);
    private void Log(string message)
    {
        if (Logger is null)
        {
            throw new InvalidOperationException();
        }

        Logger.LogInformation(message);
    }
}