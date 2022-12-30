using MessagePack;
using Microsoft.IO;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
namespace UiPath.Rpc;
using MethodExecutor = Func<object, object[], CancellationToken, Task>;
using static Expression;
using static CancellationTokenSourcePool;
using static Connection;
class Server
{
    delegate object Deserializer(ref MessagePackReader reader);
    private static readonly ConcurrentDictionary<(Type, string), Method> Methods = new();
    private static readonly ConcurrentDictionary<Type, Serializer<object>> SerializeTaskByType = new();
    private static readonly ConcurrentDictionary<Type, Deserializer> DeserializeObjectByType = new();
    private static readonly MethodInfo SerializeMethod = typeof(Server).GetStaticMethod(nameof(SerializeTaskImpl));
    private static readonly MethodInfo DeserializeMethod = typeof(Server).GetStaticMethod(nameof(DeserializeObjectImpl));
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
            Log(OnError(request, new ArgumentOutOfRangeException("endpoint", $"{Name} cannot find endpoint {request.Endpoint}")));
            return default;
        }
        try
        {
            var method = GetMethod(endpoint.Contract, request.Method);
            request.Parameters = DeserializeParameters(ref reader, method.Parameters, nestedStream, endpoint);
            var executor = method.Invoke;
            if (request.IsUpload)
            {
                return OnUploadRequest(request, endpoint, executor, nestedStream);
            }
            _ = HandleRequest(request, endpoint, executor, method.IsOneWay);
        }
        catch (Exception ex)
        {
            Log(OnError(request, ex));
            throw;
        }
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
                args[index] = arg == null ? CheckNullMessage(arg, type, endpoint) : (arg is Message message ? message.SetValues(endpoint, Client) : arg);
            }
            return args;
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
    void Log(ValueTask valueTask) => valueTask.AsTask().LogException(Logger, this);
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
            (Response, Task Result) response = default;
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
            catch (Exception ex) when(response.Result == null)
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
        return Send((new(request.Id, ex.ToError()), null), default);
    }
    ValueTask Send((Response Response, Task Result) responseResult, CancellationToken token)
    {
        if (responseResult.Result is Task<Stream> downloadStream)
        {
            responseResult = (responseResult.Response, null);
        }
        else
        {
            downloadStream = null;
        }
        var bytes = SerializeMessage(responseResult, static (in (Response, Task) responseResult, ref MessagePackWriter writer) =>
        {
            var (response, result) = responseResult;
            Serialize(response, ref writer);
            if (result != null)
            {
                SerializeTask(result, ref writer);
            }
        });
        return downloadStream == null ? _connection.SendMessage(MessageType.Response, bytes, token) : SendDownloadStream(bytes, downloadStream.Result, token);
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
    static object DeserializeObjectImpl<T>(ref MessagePackReader reader) => Deserialize<T>(ref reader);
    static void SerializeTask(Task task, ref MessagePackWriter writer) => SerializeTaskByType.GetOrAdd(task.GetType(), static resultType =>
        SerializeMethod.MakeGenericDelegate<Serializer<object>>(resultType.GenericTypeArguments[0]))(task, ref writer);
    static object DeserializeObject(Type type, ref MessagePackReader reader) => DeserializeObjectByType.GetOrAdd(type, static resultType =>
        DeserializeMethod.MakeGenericDelegate<Deserializer>(resultType))(ref reader);
    static void SerializeTaskImpl<T>(in object task, ref MessagePackWriter writer) => Serialize(((Task<T>)task).Result, ref writer);
    static Method GetMethod(Type contract, string methodName) => Methods.GetOrAdd((contract, methodName),
        static ((Type contract, string methodName) key) => new(key.contract.GetInterfaceMethod(key.methodName)));
    readonly struct Method
    {
        static readonly ParameterExpression Target = Parameter(typeof(object), "target");
        static readonly ParameterExpression Token = Parameter(typeof(CancellationToken), "token");
        static readonly ParameterExpression Arguments = Parameter(typeof(object[]), "parameters");
        static readonly ReadOnlyCollection<ParameterExpression> LambdaParams = 
            new ReadOnlyCollectionBuilder<ParameterExpression> { Target, Arguments, Token }.ToReadOnlyCollection();
        static readonly Expression FirstArg = CreateArg(0);
        static readonly Expression SecondArg = CreateArg(1);
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
                callParameters[index] = type == typeof(CancellationToken) ? Token : Convert(GetArg(index), type);
            }
            var instanceCast = Convert(Target, method.DeclaringType);
            var methodCall = Call(instanceCast, method, callParameters);
            var lambda = Lambda<MethodExecutor>(methodCall, LambdaParams);
            Invoke = lambda.Compile();
            IsOneWay = method.IsOneWay();
        }
        static Expression CreateArg(int index) => ArrayIndex(Arguments, Constant(index, typeof(int)));
        static Expression GetArg(int index) => index switch
        {
            0 => FirstArg,
            1 => SecondArg,
            _ => CreateArg(index)
        };
    }
    public readonly record struct Parameter(Type Type, object Default);
}
file readonly record struct IncomingRequest(int RequestId, object[] Parameters, EndpointSettings Endpoint, MethodExecutor Executor)
{
    TaskScheduler Scheduler => Endpoint.Scheduler;
    public Task OneWay() => Scheduler == null ? Invoke() : InvokeOnScheduler().Unwrap();
    public ValueTask<(Response, Task)> GetResponse(CancellationToken token) => Scheduler == null ? GetMethodResult(Invoke(token)) : ScheduleMethodResult(token);
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
    async ValueTask<(Response, Task)> ScheduleMethodResult(CancellationToken cancellationToken) => await GetMethodResult(await InvokeOnScheduler(cancellationToken));
    async ValueTask<(Response, Task)> GetMethodResult(Task methodResult)
    {
        await methodResult;
        return (new(RequestId), methodResult);
    }
}