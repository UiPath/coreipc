using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;

namespace UiPath.Ipc;

using static TaskCompletionPool<Response>;
using static IOHelpers;

public sealed class Connection : IDisposable
{
    private static readonly IOException ClosedException = new("Connection closed.");
    private readonly ConcurrentDictionary<string, ManualResetValueTaskSource> _requests = new();
    private long _requestCounter = -1;
    private readonly int _maxMessageSize;
    private readonly DeferredLazy<Task> _receiveLoop = new();
    private readonly SemaphoreSlim _sendLock = new(1);
    private readonly WaitCallback _onResponse;
    private readonly WaitCallback _onRequest;
    private readonly WaitCallback _onCancellation;
    private readonly Action<object?> _cancelRequest;
    private readonly byte[] _buffer = new byte[sizeof(long)];
    private readonly NestedStream _nestedStream;
    public Stream Network { get; }
    public ILogger? Logger { get; internal set; }

    [MemberNotNullWhen(returnValue: true, nameof(Logger))]
    public bool LogEnabled => Logger.Enabled();

    public string DebugName { get; }
    public ISerializer? Serializer { get; }

    public Connection(Stream network, ISerializer? serializer, ILogger? logger, string debugName, int maxMessageSize = int.MaxValue)
    {
        Network = network;
        _nestedStream = new NestedStream(network, 0);
        Serializer = serializer;
        Logger = logger;
        DebugName = $"{debugName} {GetHashCode()}";
        _maxMessageSize = maxMessageSize;
        _onResponse = response => OnResponseReceived(((Response obj, Telemetry.DeserializationSucceeded telemetry))response!);
        _onRequest = request => OnRequestReceived(((Request obj, Telemetry.DeserializationSucceeded telemetry))request!);
        _onCancellation = requestId => OnCancellationReceived(((CancellationRequest obj, Telemetry.DeserializationSucceeded telemetry))requestId!);
        _cancelRequest = requestId => CancelRequest((string)requestId!);
    }

    public override string ToString() => DebugName;
    public string NewRequestId() => Interlocked.Increment(ref _requestCounter).ToString();
    internal Task Listen(Telemetry.ConnectionListenReason telemCause)
    => _receiveLoop.GetValue(factory: () => ReceiveLoop(telemCause));

    internal event Func<Request, Telemetry.HonorRequest, ValueTask> RequestReceived;
    internal event Action<string> CancellationReceived;
    public event EventHandler<EventArgs>? Closed;
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    internal async ValueTask<Response> RemoteCall(Request request, CancellationToken token)
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
            Logger?.LogError($"Caught exception while sending the request. Ex: {ex}");
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
            } catch (Exception ex)
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
    internal ValueTask Send(Request request, CancellationToken token)
    {
        Logger?.LogInformation("Connection.Send...");
        var uploadStream = request.UploadStream;
        var requestBytes = SerializeToStream(request);
        return uploadStream == null ?
            SendMessage(MessageType.Request, requestBytes, token) :
            SendStream(MessageType.UploadRequest, requestBytes, uploadStream, token);
    }
    void CancelRequest(string requestId)
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
    internal ValueTask Send(Response response, CancellationToken cancellationToken)
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
    private async Task ReceiveLoop(Telemetry.ConnectionListenReason telemCause)
    {
        var telemReceiveLoop = new Telemetry.ReceiveLoop { ConnectionListenReasonId = telemCause.Id };
        await telemReceiveLoop.Monitor(
            async () =>
            {
                try
                {
                    while (await ReadBuffer(HeaderLength))
                    {
                        var length = BitConverter.ToInt32(_buffer, startIndex: 1);

                        var telemReceivedHeader = new Telemetry.ReceivedHeader
                        {
                            ReceiveLoopId = telemReceiveLoop.Id,
                            MessageLength = length,
                            MessageType = (MessageType)_buffer[0],
                            MaxMessageLength = _maxMessageSize,
                            SynchronizationContextIsNull = SynchronizationContext.Current is null
                        };
                        await telemReceivedHeader.Monitor(async () =>
                        {
                            Debug.Assert(SynchronizationContext.Current is null);
                            if (length > _maxMessageSize)
                            {
                                throw new InvalidDataException($"Message too large. The maximum message size is {_maxMessageSize / (1024 * 1024)} megabytes.");
                            }
                            _nestedStream.Reset(length);
                            await HandleMessage(telemReceivedHeader);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"{nameof(ReceiveLoop)} {DebugName}");
                }
                if (LogEnabled)
                {
                    Log($"{nameof(ReceiveLoop)} {DebugName} finished.");
                }
                Dispose();
                return;
            });

#if !NET461
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
        async ValueTask HandleMessage(Telemetry.ReceivedHeader telemCause)
        {
            var messageType = (MessageType)_buffer[0];
            switch (messageType)
            {
                case MessageType.Response:
                    RunAsync(_onResponse, (await Deserialize<Response>(telemCause))!);
                    break;
                case MessageType.Request:
                    RunAsync(_onRequest, (await Deserialize<Request>(telemCause))!);
                    break;
                case MessageType.CancellationRequest:
                    RunAsync(_onCancellation, (await Deserialize<CancellationRequest>(telemCause))!);
                    break;
                case MessageType.UploadRequest:
                    await OnUploadRequest(telemCause);
                    return;
                case MessageType.DownloadResponse:
                    await OnDownloadResponse(telemCause);
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
    private async Task OnDownloadResponse(Telemetry.ReceivedHeader telemCause)
    {
        var data = (await Deserialize<Response>(telemCause))!;
        await EnterStreamMode();
        var streamDisposed = new TaskCompletionSource<bool>();
        EventHandler disposedHandler = delegate { streamDisposed.TrySetResult(true); };
        _nestedStream.Disposed += disposedHandler;
        try
        {
            data.obj.DownloadStream = _nestedStream;
            OnResponseReceived(data);
            await streamDisposed.Task;
        }
        finally
        {
            _nestedStream.Disposed -= disposedHandler;
        }
    }
    private async Task OnUploadRequest(Telemetry.ReceivedHeader telemCause)
    {
        var data = (await Deserialize<Request>(telemCause))!;
        await EnterStreamMode();
        using (_nestedStream)
        {
            data.obj!.UploadStream = _nestedStream;
            await OnRequestReceived(data);
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
            Serializer.OrDefault().Serialize(value, stream);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
    private async ValueTask<(T obj, Telemetry.DeserializationSucceeded telemetry)> Deserialize<T>(Telemetry.ReceivedHeader telemCause)
    {
        var telemDeserializePayload = new Telemetry.DeserializePayload { ReceivedHeaderId = telemCause.Id, Logger = Logger }.Log();

        try
        {
            var result = (await Serializer.OrDefault().DeserializeAsync<T>(_nestedStream, Logger))!;

            string json;
            try
            {
                json = JsonConvert.SerializeObject(result, Telemetry.Jss);
            }
            catch (Exception ex)
            {
                json = JsonConvert.SerializeObject(new
                {
                    Exception = ex.ToString(),
                    ResultType = result?.GetType().AssemblyQualifiedName,
                }, Formatting.Indented);
            }

            var telemSucceeded = new Telemetry.DeserializationSucceeded { StartId = telemDeserializePayload.Id, ResultJson = json, Logger = Logger }.Log();
            return (result, telemSucceeded);
        }
        catch (Exception ex)
        {
            new Telemetry.VoidFailed { StartId = telemDeserializePayload.Id, Exception = ex }.Log();
            throw;
        }
    }
    private void OnCancellationReceived((CancellationRequest obj, Telemetry.DeserializationSucceeded telemetry) data)
    {
        try
        {
            var telemHonorCancellation = new Telemetry.HonorCancellation { Cause = data.telemetry.Id };
            telemHonorCancellation.Monitor(() =>
            {
                CancellationReceived(data.obj.RequestId);
            });
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }
    private void Log(Exception ex) => Logger.LogException(ex, DebugName);
    private async ValueTask OnRequestReceived((Request? obj, Telemetry.DeserializationSucceeded telemetry) data)
    {
        try
        {
            var telemHonorRequest = new Telemetry.HonorRequest { Cause = data.telemetry.Id, Method = data.obj!.MethodName };
            await telemHonorRequest.Monitor(async () =>
            {
                await RequestReceived(data.obj!, telemHonorRequest);
            });
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }
    private void OnResponseReceived((Response obj, Telemetry.DeserializationSucceeded telemetry) data)
    {
        try
        {
            new Telemetry.HonorResponse { Cause = data.telemetry.Id }.Monitor(() =>
            {
                if (LogEnabled)
                {
                    Log($"Received response for request {data.obj.RequestId} {DebugName}.");
                }
                if (_requests.TryRemove(data.obj.RequestId, out var completionSource))
                {
                    completionSource.SetResult(data.obj);
                }
            });
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private void Log(string message)
    {
        if (Logger is null)
        {
            throw new InvalidOperationException();
        }

        Logger.LogInformation(message);
    }
}