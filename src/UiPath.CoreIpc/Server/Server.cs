using System.Linq.Expressions;
namespace UiPath.CoreIpc;
using GetTaskResultFunc = Func<Task, object>;
using MethodExecutor = Func<object, object[], CancellationToken, Task>;
using static Expression;
using static CancellationTokenSourcePool;
class Server
{
    private static readonly MethodInfo GetResultMethod = typeof(Server).GetStaticMethod(nameof(GetTaskResultImpl));
    private static readonly ConcurrentDictionary<Type, GetTaskResultFunc> GetTaskResultByType = new();
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
    public async ValueTask OnRequestReceived(IncomingRequest incomingRequest)
    {
        var (request, method, endpoint) = incomingRequest;
        try
        {
            if (!method.ReturnType.IsGenericType)
            {
                await HandleOneWayRequest(method, endpoint, request);
                return;
            }
            Response response = default;
            var requestCancellation = Rent();
            _requests[request.Id] = requestCancellation;
            var timeout = request.GetTimeout(Settings.RequestTimeout);
            var timeoutHelper = new TimeoutHelper(timeout, requestCancellation.Token);
            try
            {
                var token = timeoutHelper.Token;
                response = await HandleRequest(method, endpoint, request, token);
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
        if (_requests.TryRemove(request.Id, out var cancellation))
        {
            cancellation.Return();
        }
    }
    static Task HandleOneWayRequest(in Method method, EndpointSettings endpoint, in Request request) =>
        endpoint.Scheduler == null ? CallMethod(method, endpoint, request, default) : CallOnScheduler(method, endpoint, request, default).Unwrap();
    static ValueTask<Response> HandleRequest(in Method method, EndpointSettings endpoint, in Request request, CancellationToken cancellationToken) =>
        endpoint.Scheduler == null ? 
            Response(request, method.ReturnType, CallMethod(method, endpoint, request, cancellationToken)) : 
            RunOnScheduler(method, endpoint, request, cancellationToken);
    static async ValueTask<Response> RunOnScheduler(Method method, EndpointSettings endpoint, Request request, CancellationToken cancellationToken) =>
        await Response(request, method.ReturnType, await CallOnScheduler(method, endpoint, request, cancellationToken));
    static Task<Task> CallOnScheduler(Method method, EndpointSettings endpoint, Request request, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(() => CallMethod(method, endpoint, request, cancellationToken), cancellationToken, TaskCreationOptions.DenyChildAttach, endpoint.Scheduler);
    static async ValueTask<Response> Response(Request request, Type returnTaskType, Task methodResult)
    {
        await methodResult;
        var returnValue = GetTaskResult(returnTaskType, methodResult);
        return new Response(request.Id) { Data = returnValue };
    }
    static Task CallMethod(in Method method, EndpointSettings endpoint, in Request request, CancellationToken cancellationToken) =>
        method.Invoke(endpoint.ServerObject(), request.Parameters, cancellationToken);
    private void Log(string message) => _connection.Log(message);
    private ILogger Logger => _connection.Logger;
    private bool LogEnabled => Logger.Enabled();
    private ListenerSettings Settings { get; }
    public IServiceProvider ServiceProvider => Settings.ServiceProvider;
    public string Name => _connection.Name;
    public IDictionary<string, EndpointSettings> Endpoints => Settings.Endpoints;
    static object GetTaskResultImpl<T>(Task task) => ((Task<T>)task).Result;
    static object GetTaskResult(Type taskType, Task task) => GetTaskResultByType.GetOrAdd(taskType, resultType =>
        GetResultMethod.MakeGenericDelegate<GetTaskResultFunc>(resultType.GenericTypeArguments[0]))(task);
}
public readonly struct Method
{
    static readonly ParameterExpression TargetParameter = Parameter(typeof(object), "target");
    static readonly ParameterExpression TokenParameter = Parameter(typeof(CancellationToken), "cancellationToken");
    static readonly ParameterExpression ParametersParameter = Parameter(typeof(object[]), "parameters");
    public readonly MethodExecutor Invoke;
    public readonly MethodInfo MethodInfo;
    public readonly ParameterInfo[] Parameters;
    public readonly object[] Defaults;
    public Type ReturnType => MethodInfo.ReturnType;
    public Method(MethodInfo method)
    {
        // https://github.com/dotnet/aspnetcore/blob/3f620310883092905ed6f13d784c908b5f4a9d7e/src/Shared/ObjectMethodExecutor/ObjectMethodExecutor.cs#L156
        var parameters = method.GetParameters();
        var parametersLength = parameters.Length;
        var callParameters = new Expression[parametersLength];
        var defaults = new object[parametersLength];
        for (int index = 0; index < parametersLength; index++)
        {
            var parameter = parameters[index];
            defaults[index] = parameter.GetDefaultValue();
            callParameters[index] = parameter.ParameterType == typeof(CancellationToken) ? TokenParameter :
                Convert(ArrayIndex(ParametersParameter, Constant(index, typeof(int))), parameter.ParameterType);
        }
        var instanceCast = Convert(TargetParameter, method.DeclaringType);
        var methodCall = Call(instanceCast, method, callParameters);
        var lambda = Lambda<MethodExecutor>(methodCall, TargetParameter, ParametersParameter, TokenParameter);
        Invoke = lambda.Compile();
        MethodInfo = method;
        Parameters = parameters;
        Defaults = defaults;
    }
    public override string ToString() => MethodInfo.ToString();
}