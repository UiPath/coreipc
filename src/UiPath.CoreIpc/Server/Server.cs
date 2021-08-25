using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq.Expressions;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
namespace UiPath.CoreIpc
{
    using GetTaskResultFunc = Func<Task, object>;
    using MethodExecutor = Func<object, object[], Task>;
    using static Expression;
    class Server
    {
        private static readonly MethodInfo GetResultMethod = typeof(Server).GetStaticMethod(nameof(GetTaskResultImpl));
        private static readonly ConcurrentDictionaryWrapper<(Type,string), Method> Methods = new(CreateMethod);
        private static readonly ConcurrentDictionaryWrapper<Type, GetTaskResultFunc> GetTaskResultByType = new(GetTaskResultFunc);
        private readonly Connection _connection;
        private readonly IClient _client;
        private readonly CancellationTokenSource _connectionClosed = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new();
        private readonly CancellationToken _hostCancellationToken;
        public Server(ListenerSettings settings, Connection connection, IClient client = null, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            _connection = connection;
            _client = client;
            _hostCancellationToken = cancellationToken;
            connection.RequestReceived += OnRequestReceived;
            connection.CancellationReceived += requestId =>
            {
                if (_requests.TryGetValue(requestId, out var cancellation))
                {
                    cancellation.Cancel();
                }
            };
            connection.Closed += delegate
            {
                if (LogEnabled)
                {
                    Log($"Server {Name} closed.");
                }
                _connectionClosed.Cancel();
            };
        }
        async Task OnRequestReceived(Request request, Stream uploadStream)
        {
            try
            {
                if (LogEnabled)
                {
                    Log($"{Name} received request {request}");
                }
                if (!Endpoints.TryGetValue(request.Endpoint, out var endpoint))
                {
                    await OnError(request, new ArgumentOutOfRangeException(nameof(request.Endpoint), $"{Name} cannot find endpoint {request.Endpoint}"));
                    return;
                }
                var method = GetMethod(endpoint.Contract, request.MethodName);
                if (!method.ReturnType.IsGenericType && request.HasObjectParameters)
                {
                    await HandleRequest(method, endpoint, request, uploadStream, default);
                    return;
                }
                Response response;
                var requestCancellation = new CancellationTokenSource();
                _requests[request.Id] = requestCancellation;
                var timeout = request.GetTimeout(Settings.RequestTimeout);
                var timeoutHelper = new TimeoutHelper(timeout, new() { _hostCancellationToken, requestCancellation.Token, _connectionClosed.Token });
                try
                {
                    var token = timeoutHelper.Token;
                    response = await HandleRequest(method, endpoint, request, uploadStream, token);
                    if (LogEnabled)
                    {
                        Log($"{Name} sending response for {request}");
                    }
                    await SendResponse(response, token);
                }
                catch (Exception ex)
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
                Logger.LogException(ex, $"{Name} {request}");
            }
            if (_requests.TryRemove(request.Id, out var cancellation))
            {
                cancellation.Dispose();
            }
            if (_connectionClosed.IsCancellationRequested)
            {
                _connectionClosed.Dispose();
            }
        }
        Task OnError(Request request, Exception ex)
        {
            Logger.LogException(ex, $"{Name} {request}");
            return SendResponse(Response.Fail(request, ex), _hostCancellationToken);
        }
        async Task<Response> HandleRequest(Method method, EndpointSettings endpoint, Request request, Stream uploadStream, CancellationToken cancellationToken)
        {
            var contract = endpoint.Contract;
            var arguments = GetArguments();
            var beforeCall = endpoint.BeforeCall;
            if (beforeCall != null)
            {
                await beforeCall(new(default, method.MethodInfo, arguments), cancellationToken);
            }
            IServiceScope scope = null;
            var service = endpoint.ServiceInstance;
            try
            {
                if (service == null)
                {
                    scope = ServiceProvider.CreateScope();
                    service = scope.ServiceProvider.GetRequiredService(contract);
                }
                return await InvokeMethod();
            }
            finally
            {
                scope?.Dispose();
            }
            async Task<Response> InvokeMethod()
            {
                var returnTaskType = method.ReturnType;
                var scheduler = endpoint.Scheduler;
                if (returnTaskType.IsGenericType)
                {
                    var methodResult = scheduler is null ? MethodCall() : await RunOnScheduler();
                    await methodResult;
                    var returnValue = GetTaskResult(returnTaskType, methodResult);
                    if (returnValue is Stream downloadStream)
                    {
                        return Response.Success(request, downloadStream);
                    }
                    return request.HasObjectParameters ? new Response(request.Id, null, null, returnValue) : Response.Success(request, Serializer.Serialize(returnValue));
                }
                else
                {
                    (scheduler is null ? MethodCall() : RunOnScheduler()).LogException(Logger, method);
                    return request.HasObjectParameters ? null : Response.Success(request, "");
                }
                Task MethodCall() => method.Invoke(service, arguments);
                Task<Task> RunOnScheduler() => Task.Factory.StartNew(MethodCall, cancellationToken, TaskCreationOptions.DenyChildAttach, scheduler);
            }
            object[] GetArguments()
            {
                var parameters = method.Parameters;
                var parametersLength = request.ParametersLength;
                if (parametersLength > parameters.Length)
                {
                    throw new ArgumentException("Too many parameters for " + method);
                }
                var allArguments = new object[parameters.Length];
                Deserialize();
                SetOptionalArguments();
                return allArguments;
                void Deserialize()
                {
                    object argument;
                    for (int index = 0; index < parametersLength; index++)
                    {
                        var parameterType = parameters[index].ParameterType;
                        if (parameterType == typeof(CancellationToken))
                        {
                            argument = cancellationToken;
                        }
                        else if (parameterType == typeof(Stream))
                        {
                            argument = uploadStream;
                        }
                        else
                        {
                            argument = request.DeserializeParameter(Serializer, index, parameterType);
                            argument = CheckMessage(argument, parameterType);
                        }
                        allArguments[index] = argument;
                    }
                }
                object CheckMessage(object argument, Type parameterType)
                {
                    if (parameterType == typeof(Message) && argument == null)
                    {
                        argument = new Message();
                    }
                    if (argument is Message message)
                    {
                        message.CallbackContract = endpoint.CallbackContract;
                        message.Client = _client;
                        message.ObjectParameters = request.HasObjectParameters;
                    }
                    return argument;
                }
                void SetOptionalArguments()
                {
                    for (int index = request.ParametersLength; index < parameters.Length; index++)
                    {
                        allArguments[index] = CheckMessage(method.Defaults[index], parameters[index].ParameterType);
                    }
                }
            }
        }
        private void Log(string message) => _connection.Log(message);
        private ILogger Logger => _connection.Logger;
        private bool LogEnabled => Logger.Enabled();
        private ListenerSettings Settings { get; }
        public IServiceProvider ServiceProvider => Settings.ServiceProvider;
        public ISerializer Serializer => _connection.Serializer;
        public string Name => _connection.Name;
        public IDictionary<string, EndpointSettings> Endpoints => Settings.Endpoints;
        Task SendResponse(Response response, CancellationToken responseCancellation) => 
            _connectionClosed.IsCancellationRequested ? Task.CompletedTask : _connection.Send(response, responseCancellation);
        static object GetTaskResultImpl<T>(Task task) => ((Task<T>)task).Result;
        static object GetTaskResult(Type taskType, Task task) => 
            GetTaskResultByType.GetOrAdd(taskType.GenericTypeArguments[0])(task);
        static GetTaskResultFunc GetTaskResultFunc(Type resultType) => GetResultMethod.MakeGenericDelegate<GetTaskResultFunc>(resultType);
        static Method GetMethod(Type contract, string methodName) => Methods.GetOrAdd((contract, methodName));
        static Method CreateMethod((Type contract,string methodName) key) => new(key.contract.GetInterfaceMethod(key.methodName));
        readonly struct Method
        {
            static readonly ParameterExpression TargetParameter = Parameter(typeof(object), "target");
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
                var callParameters = new Expression[parameters.Length];
                var defaults = new object[parameters.Length];
                for (int index = 0; index < parameters.Length; index++)
                {
                    var parameter = parameters[index];
                    defaults[index] = parameter.GetDefaultValue();
                    var paramValue = ArrayIndex(ParametersParameter, Constant(index, typeof(int)));
                    callParameters[index] = Convert(paramValue, parameter.ParameterType);
                }
                var instanceCast = Convert(TargetParameter, method.DeclaringType);
                var methodCall = Call(instanceCast, method, callParameters);
                var lambda = Lambda<MethodExecutor>(methodCall, TargetParameter, ParametersParameter);
                _executor = lambda.Compile();
                MethodInfo = method;
                Parameters = parameters;
                Defaults = defaults;
            }
            public Task Invoke(object service, object[] arguments) => _executor.Invoke(service, arguments);
            public override string ToString() => MethodInfo.ToString();
        }
    }
}