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
    private int _requestCounter;
    private readonly int _maxMessageSize;
    private readonly Lazy<Task> _receiveLoop;
    private readonly SemaphoreSlim _sendLock = new(1);
#if NET461
    private readonly WaitCallback _onResponse;
    private readonly WaitCallback _onRequest;
    private readonly WaitCallback _onCancellation;
#else
    private readonly Action<IncomingResponse> _onResponse;
    private readonly Action<IncomingRequest> _onRequest;
    private readonly Action<int> _onCancellation;
#endif
    private readonly Action<object> _cancelRequest;
    private readonly byte[] _buffer = new byte[sizeof(long)];
    private readonly NestedStream _nestedStream;
    private RecyclableMemoryStream _messageStream;
    public Connection(Stream network, ILogger logger, string name, int maxMessageSize = int.MaxValue)
    {
        Network = network;
        _nestedStream = new NestedStream(network, 0);
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
        var requestBytes = Serialize(request, static(request, writer)=>
        {
            Serialize(request, writer);
            foreach (var arg in request.Parameters)
            {
                Serialize(arg, writer);
            }
        });
        return uploadStream == null ?
            SendMessage(MessageType.Request, requestBytes, token) :
            SendStream(MessageType.Request, requestBytes, uploadStream, token);
    }
    void CancelRequest(int requestId)
    {
        CancelServerCall(requestId).LogException(Logger, this);
        Completion(requestId)?.SetCanceled();
        return;
        Task CancelServerCall(int requestId) =>
            SendMessage(MessageType.CancellationRequest, Serialize(new CancellationRequest(requestId), Serialize), default).AsTask();
    }
    ManualResetValueTaskSource Completion(int requestId) => _requests.TryRemove(requestId, out var outgoingRequest) ? outgoingRequest.Completion : null;
    internal ValueTask Send(Response response, CancellationToken cancellationToken)
    {
        if (response.Data is Stream downloadStream)
        {
            response.Data = null;
        }
        else
        {
            downloadStream = null;
        }
        var responseBytes = Serialize(response, static(response, writer)=>
        {
            Serialize(response, writer);
            Serialize(response.Data, writer);
        });
        return downloadStream == null ?
            SendMessage(MessageType.Response, responseBytes, cancellationToken) :
            SendDownloadStream(responseBytes, downloadStream, cancellationToken);
        async ValueTask SendDownloadStream(RecyclableMemoryStream responseBytes, Stream downloadStream, CancellationToken cancellationToken)
        {
            using (downloadStream)
            {
                await SendStream(MessageType.Response, responseBytes, downloadStream, cancellationToken);
            }
        }
    }
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
        Server?.CancelRequests();
        try
        {
            closedHandler.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, this);
        }
        foreach (var requestId in _requests.Keys)
        {
            Completion(requestId)?.SetException(ClosedException);
        }
    }
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    private async ValueTask<bool> ReadHeader(int length)
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
            while (await ReadHeader(HeaderLength))
            {
                Debug.Assert(SynchronizationContext.Current == null);
                var length = BitConverter.ToInt32(_buffer, startIndex: 1);
                if (length > _maxMessageSize)
                {
                    throw new InvalidDataException($"Message too large. The maximum message size is {_maxMessageSize / (1024 * 1024)} megabytes.");
                }
                _nestedStream.Reset(length);
                var stream = GetStream();
                try
                {
                    await _nestedStream.CopyToAsync(stream);
                    stream.Position = 0;
                    _messageStream = stream;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
                await HandleMessage((MessageType)_buffer[0]);
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
        ValueTask HandleMessage(MessageType messageType) => messageType switch
        {
            MessageType.Response => OnResponse(),
            MessageType.Request => OnRequest(),
            MessageType.CancellationRequest => OnCancel(),
            _ => Unknown(messageType),
        };
        ValueTask Unknown(MessageType messageType)
        {
            if (LogEnabled)
            {
                Log("Unknown message type " + messageType);
            }
            return default;
        }
    }
    private ValueTask OnCancel()
    {
        using var _ = _messageStream;
        var reader = CreateReader();
        RunAsync(_onCancellation, Deserialize<CancellationRequest>(ref reader).RequestId);
        return default;
    }
#if NET461
    static void RunAsync(WaitCallback callback, object state) => ThreadPool.UnsafeQueueUserWorkItem(callback, state);
#else
    static void RunAsync<T>(Action<T> callback, T state) => ThreadPool.UnsafeQueueUserWorkItem(callback, state, preferLocal: true);
#endif
    private ValueTask OnResponse()
    {
        var incomingResponse = DeserializeResponse();
        var response = incomingResponse.Response;
        if (response.Empty)
        {
            return default;
        }
        if (response.Data == _nestedStream)
        {
            return OnDownloadResponse(incomingResponse);
        }
        RunAsync(_onResponse, incomingResponse);
        return default;
        async ValueTask OnDownloadResponse(IncomingResponse incomingResponse)
        {
            await EnterStreamMode();
            var streamDisposed = new TaskCompletionSource<bool>();
            EventHandler disposedHandler = delegate { streamDisposed.TrySetResult(true); };
            _nestedStream.Disposed += disposedHandler;
            try
            {
                OnResponseReceived(incomingResponse);
                await streamDisposed.Task;
            }
            finally
            {
                _nestedStream.Disposed -= disposedHandler;
            }
        }
    }
    private ValueTask OnRequest()
    {
        var request = DeserializeRequest();
        if (request.Request.Parameters is [Stream,..])
        {
            return OnUploadRequest(request);
        }
        else
        {
            RunAsync(_onRequest, request);
            return default;
        }
        async ValueTask OnUploadRequest(IncomingRequest request)
        {
            await EnterStreamMode();
            using (_nestedStream)
            {
                await OnRequestReceived(request);
            }
        }
    }
    private async ValueTask EnterStreamMode()
    {
        if (!await ReadHeader(sizeof(long)))
        {
            throw ClosedException;
        }
        var userStreamLength = BitConverter.ToInt64(_buffer, startIndex: 0);
        _nestedStream.Reset(userStreamLength);
    }
    static RecyclableMemoryStream Serialize<T>(T value, Action<T, IBufferWriter<byte>> serializer)
    {
        var stream = GetStream();
        try
        {
            stream.Position = HeaderLength;
            serializer(value, stream);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
    static void Serialize<T>(T value, IBufferWriter<byte> writer) => MessagePackSerializer.Serialize(writer, value, Contractless);
    static T Deserialize<T>(ref MessagePackReader reader) => MessagePackSerializer.Deserialize<T>(ref reader, Contractless);
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
    private IncomingResponse DeserializeResponse()
    {
        using var _ = _messageStream;
        var reader = CreateReader();
        var response = Deserialize<Response>(ref reader);
        if (LogEnabled)
        {
            Log($"Received response for request {response.RequestId} {Name}.");
        }
        if (!_requests.TryRemove(response.RequestId, out var outgoingRequest))
        {
            return default;
        }
        var responseType = outgoingRequest.ResponseType;
        if (response.Error == null)
        {
            if (responseType == typeof(Stream))
            {
                reader.ReadNil();
                response.Data = _nestedStream;
            }
            else
            {
                response.Data = MessagePackSerializer.Deserialize(responseType, ref reader, Contractless);
            }
        }
        else
        {
            reader.ReadNil();
        }
        return new(response, outgoingRequest.Completion);
    }
    private MessagePackReader CreateReader() => new(_messageStream.GetReadOnlySequence());
    private IncomingRequest DeserializeRequest()
    {
        using var _ = _messageStream;
        var reader = CreateReader();
        var request = Deserialize<Request>(ref reader);
        if (LogEnabled)
        {
            Log($"{Name} received request {request}");
        }
        if (!Server.Endpoints.TryGetValue(request.Endpoint, out var endpoint))
        {
            Error(request);
            return default;
        }
        var method = GetMethod(endpoint.Contract, request.MethodName);
        var args = new object[method.Parameters.Length];
        for (int index = 0; index < args.Length; index++)
        {
            var type = method.Parameters[index].ParameterType;
            if (type == typeof(CancellationToken))
            {
                reader.ReadNil();
                continue;
            }
            else if (type == typeof(Stream))
            {
                reader.ReadNil();
                args[index] = _nestedStream;
                continue;
            }
            var arg = MessagePackSerializer.Deserialize(type, ref reader, Contractless);
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
    async void Error(Request request)
    {
        try
        {
            await OnError(request, new ArgumentOutOfRangeException(nameof(request.Endpoint), $"{Name} cannot find endpoint {request.Endpoint}"));
        }
        catch (Exception ex)
        {
            Log(ex);
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
readonly record struct IncomingRequest(Request Request, Method Method, EndpointSettings Endpoint);
readonly record struct OutgoingRequest(ManualResetValueTaskSource Completion, Type ResponseType);
readonly record struct IncomingResponse(Response Response, ManualResetValueTaskSource Completion);