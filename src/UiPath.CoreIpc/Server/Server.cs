using System.Linq.Expressions;

namespace UiPath.Ipc;

using GetTaskResultFunc = Func<Task, object>;
using MethodExecutor = Func<object, object?[], CancellationToken, Task>;
using static Expression;
using static CancellationTokenSourcePool;

internal class Server
{
    static Server()
    {
        var prototype = GetTaskResultImpl<object>;
        GetResultMethod = prototype.Method.GetGenericMethodDefinition();
    }

    private static readonly MethodInfo GetResultMethod;
    private static readonly ConcurrentDictionary<MethodKey, Method> Methods = new();
    private static readonly ConcurrentDictionary<Type, GetTaskResultFunc> GetTaskResultByType = new();

    private readonly Router _router;
    private readonly Connection _connection;
    private readonly IClient? _client;
    private readonly ConcurrentDictionary<string, PooledCancellationTokenSource> _requests = new();

    private readonly TimeSpan _requestTimeout;

    private ILogger? Logger => _connection.Logger;
    private bool LogEnabled => Logger.Enabled();
    public ISerializer Serializer => _connection.Serializer.OrDefault();
    public string DebugName => _connection.DebugName;

    public Server(Router router, TimeSpan requestTimeout, Connection connection, IClient? client = null)
    {
        _router = router;
        _requestTimeout = requestTimeout;
        _connection = connection;
        _client = client;
        connection.RequestReceived += OnRequestReceived;
        connection.CancellationReceived += CancelRequest;
        connection.Closed += delegate
        {
            if (LogEnabled)
            {
                Log($"Server {DebugName} closed.");
            }
            foreach (var requestId in _requests.Keys)
            {
                try
                {
                    CancelRequest(requestId);
                }
                catch (Exception ex)
                {
                    Logger.OrDefault().LogException(ex, $"{DebugName}");
                }
            }
        };
    }

    private void CancelRequest(string requestId)
    {
        if (_requests.TryRemove(requestId, out var cancellation))
        {
            cancellation.Cancel();
            cancellation.Return();
        }
    }

#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask OnRequestReceived(Request request)
    {
        try
        {
            if (LogEnabled)
            {
                Log($"{DebugName} received request {request}");
            }
            if (!_router.TryResolve(request.Endpoint, out var route))
            {
                await OnError(request, new EndpointNotFoundException(nameof(request.Endpoint), DebugName, request.Endpoint));
                return;
            }
            var method = GetMethod(route.Service.Type, request.MethodName);
            Response? response = null;
            var requestCancellation = Rent();
            _requests[request.Id] = requestCancellation;
            var timeout = request.GetTimeout(_requestTimeout);
            var timeoutHelper = new TimeoutHelper(timeout, requestCancellation.Token);
            try
            {
                var token = timeoutHelper.Token;
                response = await HandleRequest(method, route, request, token);

                if (LogEnabled)
                {
                    Log($"{DebugName} sending response for {request}");
                }
                await SendResponse(response, token);
            }
            catch (Exception ex) when (response is null)
            {
                await OnError(request, timeoutHelper.CheckTimeout(ex, request.MethodName));
            }
            finally
            {
                timeoutHelper.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"{DebugName} {request}");
        }
        if (_requests.TryRemove(request.Id, out var cancellation))
        {
            cancellation.Return();
        }
    }
    ValueTask OnError(Request request, Exception ex)
    {
        Logger.LogException(ex, $"{DebugName} {request}");
        return SendResponse(Response.Fail(request, ex), default);
    }

#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    private async ValueTask<Response> HandleRequest(Method method, Route route, Request request, CancellationToken cancellationToken)
    {
        var arguments = GetArguments();

        object service;
        using (route.Service.Get(out service))
        {
            return await InvokeMethod();
        }
#if !NET461
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        async ValueTask<Response> InvokeMethod()
        {
            var returnTaskType = method.ReturnType;
            var scheduler = route.Scheduler;
            var defaultScheduler = scheduler == TaskScheduler.Default;

            Debug.Assert(scheduler != null);

            if (returnTaskType.IsGenericType)
            {
                var result = await ScheduleMethodCall();
                return result switch
                {
                    Stream downloadStream => Response.Success(request, downloadStream),
                    var x => Response.Success(request, Serializer.Serialize(x))
                };
            }

            ScheduleMethodCall().LogException(Logger, method.MethodInfo);
            return Response.Success(request, "");

            Task<object?> ScheduleMethodCall() => defaultScheduler ? MethodCall() : RunOnScheduler();
            async Task<object?> MethodCall()
            {
                await (route.BeforeCall?.Invoke(
                    new CallInfo(newConnection: false, method.MethodInfo, arguments),
                    cancellationToken) ?? Task.CompletedTask);

                Task invocationTask = null!;

                invocationTask = method.Invoke(service, arguments, cancellationToken);
                await invocationTask;

                if (!returnTaskType.IsGenericType)
                {
                    return null;
                }

                return GetTaskResult(returnTaskType, invocationTask);
            }

            Task<object?> RunOnScheduler() => Task.Factory.StartNew(MethodCall, cancellationToken, TaskCreationOptions.DenyChildAttach, scheduler).Unwrap();
        }
        object?[] GetArguments()
        {
            var parameters = method.Parameters;
            var allParametersLength = parameters.Length;
            var requestParametersLength = request.Parameters.Length;
            if (requestParametersLength > allParametersLength)
            {
                throw new ArgumentException("Too many parameters for " + method.MethodInfo);
            }
            var allArguments = new object?[allParametersLength];
            Deserialize();
            SetOptionalArguments();

            return allArguments;
            void Deserialize()
            {
                object? argument;
                for (int index = 0; index < requestParametersLength; index++)
                {
                    var parameterType = parameters[index].ParameterType;
                    if (parameterType == typeof(CancellationToken))
                    {
                        argument = null;
                    }
                    else if (parameterType == typeof(Stream))
                    {
                        argument = request.UploadStream;
                    }
                    else
                    {
                        argument = Serializer.Deserialize(request.Parameters[index], parameterType);
                        argument = CheckMessage(argument, parameterType);
                    }
                    allArguments[index] = argument;
                }
            }
            object? CheckMessage(object? argument, Type parameterType)
            {
                if (parameterType == typeof(Message) && argument is null)
                {
                    argument = new Message();
                }
                if (argument is Message message)
                {
                    message.Client = _client;
                }
                return argument;
            }
            void SetOptionalArguments()
            {
                for (int index = requestParametersLength; index < allParametersLength; index++)
                {
                    allArguments[index] = CheckMessage(method.Defaults[index], parameters[index].ParameterType);
                }
            }
        }
    }

    private void Log(string message) => Logger.OrDefault().LogInformation(message);

    private ValueTask SendResponse(Response response, CancellationToken responseCancellation) => _connection.Send(response, responseCancellation);

    private static object? GetTaskResultImpl<T>(Task task) => (task as Task<T>)!.Result;

    private static object GetTaskResult(Type taskType, Task task)
    => GetTaskResultByType.GetOrAdd(
        taskType.GenericTypeArguments[0],
        GetResultMethod.MakeGenericDelegate<GetTaskResultFunc>)(task);

    private static Method GetMethod(Type contract, string methodName)
    => Methods.GetOrAdd(new(contract, methodName), Method.FromKey);

    private readonly record struct MethodKey(Type Contract, string MethodName);

    private readonly struct Method
    {
        public static Method FromKey(MethodKey key)
        {
            var methodInfo = key.Contract.GetInterfaceMethod(key.MethodName);
            return new(methodInfo);
        }

        private static readonly ParameterExpression TargetParameter = Parameter(typeof(object), "target");
        private static readonly ParameterExpression TokenParameter = Parameter(typeof(CancellationToken), "cancellationToken");
        private static readonly ParameterExpression ParametersParameter = Parameter(typeof(object[]), "parameters");

        private readonly MethodExecutor _executor;
        public readonly MethodInfo MethodInfo;
        public readonly ParameterInfo[] Parameters;
        public readonly object?[] Defaults;

        public Type ReturnType => MethodInfo.ReturnType;

        private Method(MethodInfo method)
        {
            // https://github.com/dotnet/aspnetcore/blob/3f620310883092905ed6f13d784c908b5f4a9d7e/src/Shared/ObjectMethodExecutor/ObjectMethodExecutor.cs#L156
            var parameters = method.GetParameters();
            var parametersLength = parameters.Length;
            var callParameters = new Expression[parametersLength];
            var defaults = new object?[parametersLength];
            for (int index = 0; index < parametersLength; index++)
            {
                var parameter = parameters[index];
                defaults[index] = parameter.GetDefaultValue();
                callParameters[index] = parameter.ParameterType == typeof(CancellationToken) ? TokenParameter :
                    Convert(ArrayIndex(ParametersParameter, Constant(index, typeof(int))), parameter.ParameterType);
            }
            var instanceCast = Convert(TargetParameter, method.DeclaringType!);
            var methodCall = Call(instanceCast, method, callParameters);
            var lambda = Lambda<MethodExecutor>(methodCall, TargetParameter, ParametersParameter, TokenParameter);
            _executor = lambda.Compile();
            MethodInfo = method;
            Parameters = parameters;
            Defaults = defaults;
        }

        public Task Invoke(object service, object?[] arguments, CancellationToken cancellationToken) => _executor.Invoke(service, arguments, cancellationToken);

        public override string ToString() => MethodInfo.ToString()!;
    }
}