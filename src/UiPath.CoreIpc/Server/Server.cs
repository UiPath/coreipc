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
                await HandleRequest(method, endpoint, request, default);
                return;
            }
            Response response = new();
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
    async ValueTask<Response> HandleRequest(Method method, EndpointSettings endpoint, Request request, CancellationToken cancellationToken)
    {
        var contract = endpoint.Contract;
        var beforeCall = endpoint.BeforeCall;
        if (beforeCall != null)
        {
            await beforeCall(new(default, method.MethodInfo, request.Parameters), cancellationToken);
        }
        var service = endpoint.ServiceInstance;
        if (service == null)
        {
            service = ServiceProvider.GetRequiredService(contract);
        }
        return await InvokeMethod();
#if !NET461
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        async ValueTask<Response> InvokeMethod()
        {
            var returnTaskType = method.ReturnType;
            var scheduler = endpoint.Scheduler;
            Debug.Assert(scheduler != null);
            var defaultScheduler = scheduler == TaskScheduler.Default;
            if (returnTaskType.IsGenericType)
            {
                var methodResult = defaultScheduler ? MethodCall() : await RunOnScheduler();
                await methodResult;
                var returnValue = GetTaskResult(returnTaskType, methodResult);
                return new Response(request.Id) { Data = returnValue };
            }
            else
            {
                (defaultScheduler ? MethodCall() : RunOnScheduler().Unwrap()).LogException(Logger, method.MethodInfo);
                return default;
            }
            Task MethodCall() => method.Invoke(service, request.Parameters, cancellationToken);
            Task<Task> RunOnScheduler() => Task.Factory.StartNew(MethodCall, cancellationToken, TaskCreationOptions.DenyChildAttach, scheduler);
        }
    }
    private void Log(string message) => _connection.Log(message);
    private ILogger Logger => _connection.Logger;
    private bool LogEnabled => Logger.Enabled();
    private ListenerSettings Settings { get; }
    public IServiceProvider ServiceProvider => Settings.ServiceProvider;
    public string Name => _connection.Name;
    public IDictionary<string, EndpointSettings> Endpoints => Settings.Endpoints;
    static object GetTaskResultImpl<T>(Task task) => ((Task<T>)task).Result;
    static object GetTaskResult(Type taskType, Task task) => 
        GetTaskResultByType.GetOrAdd(taskType.GenericTypeArguments[0], 
            resultType => GetResultMethod.MakeGenericDelegate<GetTaskResultFunc>(resultType))(task);
}
public readonly struct Method
{
    static readonly ParameterExpression TargetParameter = Parameter(typeof(object), "target");
    static readonly ParameterExpression TokenParameter = Parameter(typeof(CancellationToken), "cancellationToken");
    static readonly ParameterExpression ParametersParameter = Parameter(typeof(object[]), "parameters");
    readonly MethodExecutor _executor;
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
        _executor = lambda.Compile();
        MethodInfo = method;
        Parameters = parameters;
        Defaults = defaults;
    }
    public Task Invoke(object service, object[] arguments, CancellationToken cancellationToken) => _executor.Invoke(service, arguments, cancellationToken);
    public override string ToString() => MethodInfo.ToString();
}