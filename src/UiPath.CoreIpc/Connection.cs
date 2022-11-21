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
    private readonly MessagePackStreamReader _streamReader;
    public Connection(Stream network, ILogger logger, string name)
    {
        Network = network;
        _nestedStream = new NestedStream(network, 0);
        _streamReader = new(Network);
        Logger = logger;
        Name = $"{name} {GetHashCode()}";
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
        var requestBytes = Serialize(MessageType.Request, request, static(request, writer)=>
        {
            Serialize(request, writer);
            foreach (var arg in request.Parameters)
            {
                Serialize(arg, writer);
            }
        });
        return uploadStream == null ?
            SendMessage(requestBytes, token) :
            SendStream(requestBytes, uploadStream, token);
    }
    void CancelRequest(int requestId)
    {
        CancelServerCall(requestId).LogException(Logger, this);
        Completion(requestId)?.SetCanceled();
        return;
        Task CancelServerCall(int requestId) =>
            SendMessage(Serialize(MessageType.CancellationRequest, new CancellationRequest(requestId), Serialize), default).AsTask();
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
        var responseBytes = Serialize(MessageType.Response, response, static(response, writer)=>
        {
            Serialize(response, writer);
            Serialize(response.Data, writer);
        });
        return downloadStream == null ?
            SendMessage(responseBytes, cancellationToken) :
            SendDownloadStream(responseBytes, downloadStream, cancellationToken);
        async ValueTask SendDownloadStream(RecyclableMemoryStream responseBytes, Stream downloadStream, CancellationToken cancellationToken)
        {
            using (downloadStream)
            {
                await SendStream(responseBytes, downloadStream, cancellationToken);
            }
        }
    }
    private async ValueTask SendStream(RecyclableMemoryStream data, Stream userStream, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        CancellationTokenRegistration tokenRegistration = default;
        try
        {
            tokenRegistration = cancellationToken.UnsafeRegister(state => ((Connection)state).Dispose(), this);
            await Network.WriteMessage(data, cancellationToken);
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
    private async ValueTask SendMessage(RecyclableMemoryStream data, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await Network.WriteMessage(data, CancellationToken.None);
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
            ReadOnlySequence<byte>? bytes;
            while ((bytes = await _streamReader.ReadAsync(default)) != null)
            {
                Debug.Assert(SynchronizationContext.Current == null);
                if (bytes.Value.Length != 1)
                {
                    throw new InvalidOperationException("Invalid message header!");
                }
                var messageType = (MessageType)bytes.Value.First.Span[0];
                await HandleMessage(messageType);
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
    private async ValueTask OnCancel() => RunAsync(_onCancellation, (await Deserialize<CancellationRequest>()).RequestId);
#if NET461
    static void RunAsync(WaitCallback callback, object state) => ThreadPool.UnsafeQueueUserWorkItem(callback, state);
#else
    static void RunAsync<T>(Action<T> callback, T state) => ThreadPool.UnsafeQueueUserWorkItem(callback, state, preferLocal: true);
#endif
    private async ValueTask OnResponse()
    {
        var incomingResponse = await DeserializeResponse();
        var response = incomingResponse.Response;
        if (response.Empty)
        {
            return;
        }
        if (response.Data == _nestedStream)
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
        else
        {
            RunAsync(_onResponse, incomingResponse);
        }
    }
    private async ValueTask OnRequest()
    {
        var request = await DeserializeRequest();
        if (request.Request.Parameters is [Stream,..])
        {
            await EnterStreamMode();
            using (_nestedStream)
            {
                await OnRequestReceived(request);
            }
        }
        else
        {
            RunAsync(_onRequest, request);
        }
    }
    private async Task EnterStreamMode()
    {
        if (!await ReadHeader(sizeof(long)))
        {
            throw ClosedException;
        }
        var userStreamLength = BitConverter.ToInt64(_buffer, startIndex: 0);
        _nestedStream.Reset(userStreamLength);
    }
    static RecyclableMemoryStream Serialize<T>(MessageType messageType, T value, Action<T, IBufferWriter<byte>> serializer)
    {
        var stream = GetStream();
        try
        {
            Serialize((byte)messageType, stream);
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
        if (!_requests.TryRemove(response.RequestId, out var outgoingRequest))
        {
            return default;
        }
        var bytes = await ReadBytes();
        var responseType = outgoingRequest.ResponseType;
        if (response.Error == null)
        {
            response.Data = responseType == typeof(Stream) ? _nestedStream : MessagePackSerializer.Deserialize(responseType, bytes, Contractless);
        }
        return new(response, outgoingRequest.Completion); ;
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
readonly record struct IncomingRequest(Request Request, Method Method, EndpointSettings Endpoint);
readonly record struct OutgoingRequest(ManualResetValueTaskSource Completion, Type ResponseType);
readonly record struct IncomingResponse(Response Response, ManualResetValueTaskSource Completion);