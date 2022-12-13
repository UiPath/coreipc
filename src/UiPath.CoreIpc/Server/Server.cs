using MessagePack;
using System.Linq.Expressions;
namespace UiPath.CoreIpc;
using MethodExecutor = Func<object, object[], CancellationToken, Task>;
using static Expression;
using static CancellationTokenSourcePool;
using static UiPath.CoreIpc.Connection;
class Server
{
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
    public IClient Client { get; }
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
    public void CancelRequest(int requestId)
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
    public async ValueTask OnRequestReceived(Request request, EndpointSettings endpoint, MethodExecutor executor, bool isOneWay)
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
                await _connection.Send(response, token);
            }
            catch (Exception ex) when(response.Empty)
            {
                await _connection.OnError(request, timeoutHelper.CheckTimeout(ex, request.Method));
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
    private void Log(string message) => _connection.Log(message);
    private ILogger Logger => _connection.Logger;
    private bool LogEnabled => Logger.Enabled();
    private ListenerSettings Settings { get; }
    public string Name => _connection.Name;
    public IDictionary<string, EndpointSettings> Endpoints => Settings.Endpoints;
    public static void SerializeTask(object task, ref MessagePackWriter writer) => SerializeTaskByType.GetOrAdd(task.GetType(), static resultType =>
        SerializeMethod.MakeGenericDelegate<Serializer<object>>(resultType.GenericTypeArguments[0]))(task, ref writer);
    public static object DeserializeObject(Type type, ref MessagePackReader reader) => DeserializeObjectByType.GetOrAdd(type, static resultType =>
        DeserializeMethod.MakeGenericDelegate<Deserializer>(resultType))(ref reader);
    static void SerializeTaskImpl<T>(in object task, ref MessagePackWriter writer) => Serialize(((Task<T>)task).Result, ref writer);
}
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
public readonly struct Method
{
    static readonly ParameterExpression TargetParameter = Parameter(typeof(object), "target");
    static readonly ParameterExpression TokenParameter = Parameter(typeof(CancellationToken), "cancellationToken");
    static readonly ParameterExpression ParametersParameter = Parameter(typeof(object[]), "parameters");
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
            Parameters[index] = new(parameter);
            callParameters[index] = parameter.ParameterType == typeof(CancellationToken) ? TokenParameter :
                Convert(ArrayIndex(ParametersParameter, Constant(index, typeof(int))), parameter.ParameterType);
        }
        var instanceCast = Convert(TargetParameter, method.DeclaringType);
        var methodCall = Call(instanceCast, method, callParameters);
        var lambda = Lambda<MethodExecutor>(methodCall, TargetParameter, ParametersParameter, TokenParameter);
        Invoke = lambda.Compile();
        IsOneWay = method.IsOneWay();
    }
    public readonly record struct Parameter(Type Type, object Default)
    {
        public Parameter(ParameterInfo parameter) : this(parameter.ParameterType, parameter.GetDefaultValue()) { }
    }
}