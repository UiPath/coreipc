using MessagePack;
using Microsoft.IO;
using System.Linq.Expressions;
namespace UiPath.CoreIpc;
using MethodExecutor = Func<object, object[], CancellationToken, Task>;
using static Expression;
using static CancellationTokenSourcePool;
using static Connection;
class Server
{
    private static readonly ConcurrentDictionary<(Type, string), Method> Methods = new();
    private static readonly ConcurrentDictionary<Type, Serializer<object>> SerializeTaskByType = new();
    private static readonly ConcurrentDictionary<Type, Deserializer> DeserializeObjectByType = new();
    private static readonly MethodInfo SerializeMethod = typeof(Server).GetStaticMethod(nameof(SerializeTaskImpl));
    private static readonly MethodInfo DeserializeMethod = typeof(Connection).GetStaticMethod(nameof(DeserializeObjectImpl));
    private readonly Connection _connection;
    private readonly ConcurrentDictionary<int, PooledCancellationTokenSource> _requests = new();
    public Server(ListenerSettings settings, Connection connection, IClient client)
    {
        Settings = settings;
        _connection = connection;
        Client = client;
    }
    IClient Client { get; }
    public void CancelRequests()
    {
        if (LogEnabled)
        {
            Log($"Server {Name} closed.");
        }
        foreach (var requestId in _requests.Keys)
        {
            CancelRequest(requestId);
        }
    }
    public ValueTask OnCancel()
    {
        var request = _connection.DeserializeMessage<CancellationRequest>(out var _);
        CancelRequest(request.RequestId);
        return default;
    }
    private void CancelRequest(int requestId)
    {
        try
        {
            if (_requests.TryRemove(requestId, out var cancellation))
            {
                cancellation.Cancel();
                cancellation.Return();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"{Name}");
        }
    }
    public ValueTask OnRequest(Stream nestedStream)
    {
        var request = _connection.DeserializeMessage<Request>(out var reader);
        if (LogEnabled)
        {
            Log($"{Name} received request {request}");
        }
        if (!Endpoints.TryGetValue(request.Endpoint, out var endpoint))
        {
            OnError(request, new ArgumentOutOfRangeException("endpoint", $"{Name} cannot find endpoint {request.Endpoint}")).AsTask().LogException(Logger, this);
            return default;
        }
        var method = GetMethod(endpoint.Contract, request.Method);
        request.Parameters = DeserializeParameters(ref reader, method.Parameters, nestedStream, endpoint);
        var executor = method.Invoke;
        if (request.IsUpload)
        {
            return OnUploadRequest(request, endpoint, executor, nestedStream);
        }
        _ = HandleRequest(request, endpoint, executor, method.IsOneWay);
        return default;
        object[] DeserializeParameters(ref MessagePackReader reader, Parameter[] parameters, Stream stream, EndpointSettings endpoint)
        {
            var args = new object[parameters.Length];
            for (int index = 0; index < args.Length; index++)
            {
                var parameter = parameters[index];
                var type = parameter.Type;
                if (type == typeof(CancellationToken))
                {
                    continue;
                }
                else if (type == typeof(Stream))
                {
                    args[index] = stream;
                    continue;
                }
                if (reader.End)
                {
                    args[index] = CheckNullMessage(parameter.Default, type, endpoint);
                    continue;
                }
                var arg = DeserializeObject(type, ref reader);
                args[index] = CheckMessage(arg, type, endpoint);
            }
            return args;
            object CheckMessage(object argument, Type parameterType, EndpointSettings endpoint) => argument == null ?
                CheckNullMessage(argument, parameterType, endpoint) : (argument is Message message ? message.SetValues(endpoint, Client) : argument);
            object CheckNullMessage(object argument, Type parameterType, EndpointSettings endpoint) => parameterType == typeof(Message) ?
                new Message().SetValues(endpoint, Client) : argument;
        }
        async ValueTask OnUploadRequest(Request request, EndpointSettings endpoint, MethodExecutor executor, Stream nestedStream)
        {
            await _connection.EnterStreamMode();
            await HandleRequest(request, endpoint, executor, isOneWay: false);
            nestedStream.Dispose();
        }
    }
    async ValueTask HandleRequest(Request request, EndpointSettings endpoint, MethodExecutor executor, bool isOneWay)
    {
        int requestId = request.Id;
        IncomingRequest incomingRequest = new(requestId, request.Parameters, endpoint, executor);
        try
        {
            if (isOneWay)
            {
                await incomingRequest.OneWay();
                return;
            }
            Response response = default;
            var requestCancellation = Rent();
            _requests[requestId] = requestCancellation;
            var timeout = request.GetTimeout(Settings.RequestTimeout);
            var timeoutHelper = new TimeoutHelper(timeout, requestCancellation.Token);
            try
            {
                var token = timeoutHelper.Token;
                response = await incomingRequest.GetResponse(token);
                if (LogEnabled)
                {
                    Log($"{Name} sending response for {request}");
                }
                await Send(response, token);
            }
            catch (Exception ex) when(response.Empty)
            {
                await OnError(request, timeoutHelper.CheckTimeout(ex, request.Method));
            }
            finally
            {
                timeoutHelper.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"{Name} {request}");
        }
        if (_requests.TryRemove(requestId, out var cancellation))
        {
            cancellation.Return();
        }
    }
    ValueTask OnError(in Request request, Exception ex)
    {
        Logger.LogException(ex, $"{Name} {request}");
        return Send(new(request.Id, ex.ToError()), default);
    }
    ValueTask Send(Response response, CancellationToken cancellationToken)
    {
        if (response.Data is Task<Stream> downloadStream)
        {
            response.Data = null;
        }
        else
        {
            downloadStream = null;
        }
        var responseBytes = SerializeMessage(response, static (in Response response, ref MessagePackWriter writer) =>
        {
            Serialize(response, ref writer);
            var data = response.Data;
            if (data == null)
            {
                return;
            }
            SerializeTask(data, ref writer);
        });
        return downloadStream == null ?
            _connection.SendMessage(MessageType.Response, responseBytes, cancellationToken) :
            SendDownloadStream(responseBytes, downloadStream.Result, cancellationToken);
        async ValueTask SendDownloadStream(RecyclableMemoryStream responseBytes, Stream downloadStream, CancellationToken cancellationToken)
        {
            using (downloadStream)
            {
                await _connection.SendStream(MessageType.Response, responseBytes, downloadStream, cancellationToken);
            }
        }
    }
    private void Log(string message) => _connection.Log(message);
    private ILogger Logger => _connection.Logger;
    private bool LogEnabled => Logger.Enabled();
    private ListenerSettings Settings { get; }
    string Name => _connection.Name;
    public IDictionary<string, EndpointSettings> Endpoints => Settings.Endpoints;
    static void SerializeTask(object task, ref MessagePackWriter writer) => SerializeTaskByType.GetOrAdd(task.GetType(), static resultType =>
        SerializeMethod.MakeGenericDelegate<Serializer<object>>(resultType.GenericTypeArguments[0]))(task, ref writer);
    static object DeserializeObject(Type type, ref MessagePackReader reader) => DeserializeObjectByType.GetOrAdd(type, static resultType =>
        DeserializeMethod.MakeGenericDelegate<Deserializer>(resultType))(ref reader);
    static void SerializeTaskImpl<T>(in object task, ref MessagePackWriter writer) => Serialize(((Task<T>)task).Result, ref writer);
    static Method GetMethod(Type contract, string methodName) => Methods.GetOrAdd((contract, methodName),
        static ((Type contract, string methodName) key) => new(key.contract.GetInterfaceMethod(key.methodName)));
    readonly record struct IncomingRequest(int RequestId, object[] Parameters, EndpointSettings Endpoint, MethodExecutor Executor)
    {
        TaskScheduler Scheduler => Endpoint.Scheduler;
        public Task OneWay() => Scheduler == null ? Invoke() : InvokeOnScheduler().Unwrap();
        public ValueTask<Response> GetResponse(CancellationToken token) => Scheduler == null ? GetMethodResult(Invoke(token)) : ScheduleMethodResult(token);
        Task Invoke(CancellationToken token = default) => Executor.Invoke(Endpoint.ServerObject(), Parameters, token);
        record InvokeState(in IncomingRequest Request, CancellationToken Token)
        {
            public static Task Invoke(object state)
            {
                var (request, token) = (InvokeState)state;
                return request.Invoke(token);
            }
        }
        Task<Task> InvokeOnScheduler(CancellationToken token = default) =>
            Task.Factory.StartNew(InvokeState.Invoke, new InvokeState(this, token), token, TaskCreationOptions.DenyChildAttach, Scheduler);
        async ValueTask<Response> ScheduleMethodResult(CancellationToken cancellationToken) => await GetMethodResult(await InvokeOnScheduler(cancellationToken));
        async ValueTask<Response> GetMethodResult(Task methodResult)
        {
            await methodResult;
            return new(RequestId) { Data = methodResult };
        }
    }
    readonly struct Method
    {
        static readonly ParameterExpression Target = Parameter(typeof(object), "target");
        static readonly ParameterExpression Token = Parameter(typeof(CancellationToken), "cancellationToken");
        static readonly ParameterExpression Arguments = Parameter(typeof(object[]), "parameters");
        public readonly MethodExecutor Invoke;
        public readonly bool IsOneWay;
        public readonly Parameter[] Parameters;
        public Method(MethodInfo method)
        {
            // https://github.com/dotnet/aspnetcore/blob/3f620310883092905ed6f13d784c908b5f4a9d7e/src/Shared/ObjectMethodExecutor/ObjectMethodExecutor.cs#L156
            var parameters = method.GetParameters();
            var parametersLength = parameters.Length;
            var callParameters = new Expression[parametersLength];
            Parameters = new Parameter[parametersLength];
            for (int index = 0; index < parametersLength; index++)
            {
                var parameter = parameters[index];
                var type = parameter.ParameterType;
                Parameters[index] = new(type, parameter.GetDefaultValue());
                callParameters[index] = type == typeof(CancellationToken) ? Token : Convert(ArrayIndex(Arguments, Constant(index, typeof(int))), type);
            }
            var instanceCast = Convert(Target, method.DeclaringType);
            var methodCall = Call(instanceCast, method, callParameters);
            var lambda = Lambda<MethodExecutor>(methodCall, Target, Arguments, Token);
            Invoke = lambda.Compile();
            IsOneWay = method.IsOneWay();
        }
    }
    public readonly record struct Parameter(Type Type, object Default);
}