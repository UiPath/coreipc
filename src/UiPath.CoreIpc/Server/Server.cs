using System.Linq.Expressions;
namespace UiPath.CoreIpc;
using MethodExecutor = Func<object, object[], CancellationToken, Task>;
using static Expression;
using static CancellationTokenSourcePool;
class Server
{
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
            if (incomingRequest.IsOneWay)
            {
                await incomingRequest.HandleOneWayRequest();
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
                response = await incomingRequest.HandleRequest(token);
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
    private void Log(string message) => _connection.Log(message);
    private ILogger Logger => _connection.Logger;
    private bool LogEnabled => Logger.Enabled();
    private ListenerSettings Settings { get; }
    public string Name => _connection.Name;
    public IDictionary<string, EndpointSettings> Endpoints => Settings.Endpoints;
}
readonly record struct IncomingRequest(in Request Request, in Method Method, EndpointSettings Endpoint)
{
    TaskScheduler Scheduler => Endpoint.Scheduler;
    public bool IsOneWay => Method.IsOneWay;
    public bool IsUpload => Request.Parameters is [Stream, ..];
    public Task HandleOneWayRequest() => Scheduler == null ? Invoke() : InvokeOnScheduler().Unwrap();
    public ValueTask<Response> HandleRequest(CancellationToken token) => Scheduler == null ? GetMethodResult(Invoke(token)) : ScheduleMethodResult(token);
    Task Invoke(CancellationToken token = default) => Method.Invoke(Endpoint.ServerObject(), Request.Parameters, token);
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
        return new(Request.Id) { Data = methodResult };
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