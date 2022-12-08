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
        var request = incomingRequest.Request;
        try
        {
            if (!incomingRequest.ReturnType.IsGenericType)
            {
                await HandleOneWayRequest(incomingRequest);
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
                response = await HandleRequest(incomingRequest, token);
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
    static Task HandleOneWayRequest(in IncomingRequest incomingRequest) =>
        incomingRequest.Scheduler == null ? CallMethod(incomingRequest, default) : CallOnScheduler(incomingRequest, default).Unwrap();
    static ValueTask<Response> HandleRequest(in IncomingRequest incomingRequest, CancellationToken cancellationToken) =>
        incomingRequest.Scheduler == null ? 
            Response(incomingRequest.Request.Id, incomingRequest.ReturnType, CallMethod(incomingRequest, cancellationToken)) : 
            RunOnScheduler(incomingRequest, cancellationToken);
    static async ValueTask<Response> RunOnScheduler(IncomingRequest incomingRequest, CancellationToken cancellationToken) =>
        await Response(incomingRequest.Request.Id, incomingRequest.ReturnType, await CallOnScheduler(incomingRequest, cancellationToken));
    static Task<Task> CallOnScheduler(IncomingRequest incomingRequest, CancellationToken token) =>
        Task.Factory.StartNew(() => CallMethod(incomingRequest, token), token, TaskCreationOptions.DenyChildAttach, incomingRequest.Scheduler);
    static async ValueTask<Response> Response(int requestId, Type returnTaskType, Task methodResult)
    {
        await methodResult;
        var returnValue = GetTaskResult(returnTaskType, methodResult);
        return new Response(requestId) { Data = returnValue };
    }
    static Task CallMethod(in IncomingRequest incomingRequest, CancellationToken cancellationToken) =>
        incomingRequest.Method.Invoke(incomingRequest.Endpoint.ServerObject(), incomingRequest.Request.Parameters, cancellationToken);
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