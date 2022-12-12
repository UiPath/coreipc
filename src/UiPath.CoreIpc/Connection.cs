using MessagePack;
using Microsoft.IO;
namespace UiPath.CoreIpc;
using MethodExecutor = Func<object, object[], CancellationToken, Task>;
using static TaskCompletionPool<Response>;
using static IOHelpers;
public sealed class Connection : IDisposable
{
    static readonly MessagePackSerializerOptions Contractless = MessagePack.Resolvers.ContractlessStandardResolver.Options;
    private static readonly ConcurrentDictionary<Type, Serializer<object>> SerializeTaskByType = new();
    private static readonly ConcurrentDictionary<Type, Deserializer> DeserializeObjectByType = new();
    private static readonly MethodInfo SerializeMethod = typeof(Connection).GetStaticMethod(nameof(SerializeTaskImpl));
    private static readonly MethodInfo DeserializeMethod = typeof(Connection).GetStaticMethod(nameof(DeserializeObjectImpl));
    static readonly ConcurrentDictionary<(Type, string), Method> Methods = new();
    private static readonly IOException ClosedException = new("Connection closed.");
    private readonly ConcurrentDictionary<int, OutgoingRequest> _requests = new();
    private int _requestCounter;
    private readonly int _maxMessageSize;
    private readonly Lazy<Task> _receiveLoop;
    private readonly SemaphoreSlim _sendLock = new(1);
    private readonly Action<object> _cancelRequest;
    private readonly byte[] _header = new byte[sizeof(long)];
    private readonly NestedStream _nestedStream;
    private RecyclableMemoryStream _messageStream;
    private int _requestIdToCancel;
    internal Connection(Stream network, ILogger logger, string name, int maxMessageSize = int.MaxValue)
    {
        Network = network;
        _nestedStream = new NestedStream(network, 0);
        Logger = logger;
        Name = $"{name} {GetHashCode()}";
        _maxMessageSize = maxMessageSize;
        _cancelRequest = CancelRequest;
        _receiveLoop = new(ReceiveLoop);
        void CancelRequest(object state)
        {
            var requestId = state == null ? _requestIdToCancel : (int)state;
            var data = SerializeMessage(new CancellationRequest(requestId), Serialize);
            SendMessage(MessageType.CancellationRequest, data, default).AsTask().LogException(Logger, this);
            Completion(requestId)?.SetCanceled();
        }
    }
    internal Server Server { get; private set; }
    Stream Network { get; }
    internal ILogger Logger { get; set; }
    internal bool LogEnabled => Logger.Enabled();
    internal string Name { get; }
    public override string ToString() => Name;
    internal int NewRequestId() => Interlocked.Increment(ref _requestCounter);
    internal Task Listen() => _receiveLoop.Value;
    public event EventHandler<EventArgs> Closed = delegate{};
    internal void SetServer(ListenerSettings settings, IClient client = null) => Server = new(settings, this, client);
    internal async ValueTask<Response> RemoteCall(Request request, Type responseType, CancellationToken token)
    {
        var requestCompletion = Rent();
        var requestId = request.Id;
        _requests[requestId] = new(requestCompletion, responseType);
        object cancellationState = Interlocked.CompareExchange(ref _requestIdToCancel, requestId, 0) == 0 ? null : requestId;
        var tokenRegistration = token.UnsafeRegister(_cancelRequest, cancellationState);
        try
        {
            await Send(request, token);
        }
        catch
        {
            Completion(requestId)?.Return();
            tokenRegistration.Dispose();
            Reset(cancellationState);
            throw;
        }
        try
        {
            return await requestCompletion.ValueTask();
        }
        finally
        {
            _requests.TryRemove(requestId, out _);
            Reset(cancellationState);
            tokenRegistration.Dispose();
            requestCompletion.Return();
        }
        void Reset(object cancellationState)
        {
            if (cancellationState == null)
            {
                _requestIdToCancel = 0;
            }
        }
    }
    internal ValueTask Send(in Request request, CancellationToken token)
    {
        if (request.Parameters is [Stream uploadStream, ..])
        {
            request.Parameters[0] = null;
        }
        else
        {
            uploadStream = null;
        }
        var requestBytes = SerializeMessage(request, static(in Request request, ref MessagePackWriter writer)=>
        {
            Serialize(request, ref writer);
            foreach (var arg in request.Parameters)
            {
                Serialize(arg, ref writer);
            }
        });
        return uploadStream == null ?
            SendMessage(MessageType.Request, requestBytes, token) :
            SendStream(MessageType.Request, requestBytes, uploadStream, token);
    }
    ManualResetValueTaskSource Completion(int requestId) => _requests.TryRemove(requestId, out var outgoingRequest) ? outgoingRequest.Completion : null;
    internal ValueTask Send(Response response, CancellationToken cancellationToken)
    {
        if (response.Data is Task<Stream> downloadStream)
        {
            response.Data = null;
        }
        else
        {
            downloadStream = null;
        }
        var responseBytes = SerializeMessage(response, static(in Response response, ref MessagePackWriter writer)=>
        {
            Serialize(response, ref writer);
            var data = response.Data;
            if (data == null)
            {
                writer.WriteNil();
                return;
            }
            SerializeTask(data, ref writer);
        });
        return downloadStream == null ?
            SendMessage(MessageType.Response, responseBytes, cancellationToken) :
            SendDownloadStream(responseBytes, downloadStream.Result, cancellationToken);
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
            var lengthBytes = BitConverter.GetBytes(userStream.Length);
#if NET461
            await Network.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);
#else
            await Network.WriteAsync(lengthBytes, cancellationToken);
#endif
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
            Log(ex);
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
                _header, offset, toRead);
#else
                _header.AsMemory(offset, toRead));
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
                ValueTask messageTask;
                using (_messageStream = NewMessage())
                {
#if NET461
                    await _nestedStream.CopyToAsync(_messageStream);
#else
                    int read;
                    Memory<byte> memory;
                    while (true)
                    {
                        memory = _messageStream.GetMemory();
                        if ((read = await _nestedStream.ReadAsync(memory)) == 0)
                        {
                            break;
                        }
                        _messageStream.Advance(read);
                    }
#endif
                    _messageStream.Position = 0;
                    messageTask = HandleMessage((MessageType)_header[0]);
                }
                await messageTask;
            }
        }
        catch (Exception ex)
        {
            Log(ex);
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
        RecyclableMemoryStream NewMessage()
        {
            Debug.Assert(SynchronizationContext.Current == null);
            var length = BitConverter.ToInt32(_header, startIndex: 1);
            if (length > _maxMessageSize)
            {
                throw new InvalidDataException($"Message too large. The maximum message size is {_maxMessageSize / (1024 * 1024)} megabytes.");
            }
            _nestedStream.Reset(length);
            return GetStream(length);
        }
    }
    private ValueTask OnCancel()
    {
        var reader = CreateReader();
        Server.CancelRequest(Deserialize<CancellationRequest>(ref reader).RequestId);
        return default;
    }
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
        incomingResponse.CompleteRequest();
        return default;
        async ValueTask OnDownloadResponse(IncomingResponse incomingResponse)
        {
            await EnterStreamMode();
            var streamDisposed = new TaskCompletionSource<bool>();
            EventHandler disposedHandler = delegate { streamDisposed.TrySetResult(true); };
            _nestedStream.Disposed += disposedHandler;
            incomingResponse.CompleteRequest();
            await streamDisposed.Task;
            _nestedStream.Disposed -= disposedHandler;
        }
    }
    private ValueTask OnRequest()
    {
        var (request, endpoint, executor, isOneWay) = DeserializeRequest();
        if (endpoint == null)
        {
            return default;
        }
        if (request.IsUpload)
        {
            return OnUploadRequest(request, endpoint, executor);
        }
        else
        {
            _=Server.OnRequestReceived(request, endpoint, executor, isOneWay);
            return default;
        }
        async ValueTask OnUploadRequest(Request request, EndpointSettings endpoint, MethodExecutor executor)
        {
            await EnterStreamMode();
            await Server.OnRequestReceived(request, endpoint, executor, isOneWay: false);
            _nestedStream.Dispose();
        }
    }
    private async ValueTask EnterStreamMode()
    {
        if (!await ReadHeader(sizeof(long)))
        {
            throw ClosedException;
        }
        var userStreamLength = BitConverter.ToInt64(_header, startIndex: 0);
        _nestedStream.Reset(userStreamLength);
    }
    delegate void Serializer<T>(in T value, ref MessagePackWriter writer);
    delegate object Deserializer(ref MessagePackReader reader);
    static RecyclableMemoryStream SerializeMessage<T>(in T value, Serializer<T> serializer)
    {
        var stream = GetStream();
        try
        {
            stream.Position = HeaderLength;
            var writer = new MessagePackWriter(stream);
            serializer(value, ref writer);
            writer.Flush();
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
    static void Serialize<T>(in T value, ref MessagePackWriter writer) => MessagePackSerializer.Serialize(ref writer, value, Contractless);
    static T Deserialize<T>(ref MessagePackReader reader) => MessagePackSerializer.Deserialize<T>(ref reader, Contractless);
    private void Log(Exception ex) => Logger.LogException(ex, Name);
    private IncomingResponse DeserializeResponse()
    {
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
                response.Data = DeserializeObject(responseType, ref reader);
            }
        }
        else
        {
            reader.ReadNil();
        }
        return new(response, outgoingRequest.Completion);
    }
    private MessagePackReader CreateReader() => new(_messageStream.GetReadOnlySequence());
    private (Request, EndpointSettings, MethodExecutor, bool) DeserializeRequest()
    {
        var reader = CreateReader();
        var request = Deserialize<Request>(ref reader);
        if (LogEnabled)
        {
            Log($"{Name} received request {request}");
        }
        if (!Server.Endpoints.TryGetValue(request.Endpoint, out var endpoint))
        {
            OnError(request, new ArgumentOutOfRangeException("endpoint", $"{Name} cannot find endpoint {request.Endpoint}")).AsTask().LogException(Logger, this);
            return default;
        }
        var method = GetMethod(endpoint.Contract, request.Method);
        var args = new object[method.Parameters.Length];
        for (int index = 0; index < args.Length; index++)
        {
            var parameter = method.Parameters[index];
            var type = parameter.Type;
            if (reader.End)
            {
                args[index] = CheckMessage(parameter.Default, type, endpoint);
                continue;
            }
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
            var arg = DeserializeObject(type, ref reader);
            args[index] = CheckMessage(arg, type, endpoint);
        }
        request.Parameters = args;
        return (request, endpoint, method.Invoke, method.IsOneWay);
        object CheckMessage(object argument, Type parameterType, EndpointSettings endpoint)
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
    internal ValueTask OnError(in Request request, Exception ex)
    {
        Logger.LogException(ex, $"{Name} {request}");
        return Send(new(request.Id, ex.ToError()), default);
    }
    internal void Log(string message) => Logger.LogInformation(message);
    static Method GetMethod(Type contract, string methodName) => Methods.GetOrAdd((contract, methodName),
        static ((Type contract, string methodName) key) => new(key.contract.GetInterfaceMethod(key.methodName)));
    static void SerializeTaskImpl<T>(in object task, ref MessagePackWriter writer) => Serialize(((Task<T>)task).Result, ref writer);
    static object DeserializeObjectImpl<T>(ref MessagePackReader reader) => Deserialize<T>(ref reader);
    static void SerializeTask(object task, ref MessagePackWriter writer) => SerializeTaskByType.GetOrAdd(task.GetType(), static resultType =>
        SerializeMethod.MakeGenericDelegate<Serializer<object>>(resultType.GenericTypeArguments[0]))(task, ref writer);
    static object DeserializeObject(Type type, ref MessagePackReader reader) => DeserializeObjectByType.GetOrAdd(type, static resultType =>
        DeserializeMethod.MakeGenericDelegate<Deserializer>(resultType))(ref reader);
}
readonly record struct OutgoingRequest(ManualResetValueTaskSource Completion, Type ResponseType);
readonly record struct IncomingResponse(in Response Response, ManualResetValueTaskSource Completion)
{
    public void CompleteRequest() => Completion.SetResult(Response);
}