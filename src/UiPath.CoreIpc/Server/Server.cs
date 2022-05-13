﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq.Expressions;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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
        private readonly ActivitySource _activitySource = new ActivitySource(Connection.ActivitySourceName);

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
#if !NET461
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
        async ValueTask OnRequestReceived(Request request)
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
                if (request.HasObjectParameters && !method.ReturnType.IsGenericType)
                {
                    await HandleRequest(method, endpoint, request, default);
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
                    response = await HandleRequest(method, endpoint, request, token);
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
        ValueTask OnError(Request request, Exception ex)
        {
            Logger.LogException(ex, $"{Name} {request}");
            return SendResponse(Response.Fail(request, ex), _hostCancellationToken);
        }
#if !NET461
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        async ValueTask<Response> HandleRequest(Method method, EndpointSettings endpoint, Request request, CancellationToken cancellationToken)
        {
            var objectParameters = request.HasObjectParameters;
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
                    if (returnValue is Stream downloadStream)
                    {
                        return Response.Success(request, downloadStream);
                    }
                    return objectParameters ? new Response(request.Id, objectData: returnValue) : Response.Success(request, Serializer.Serialize(returnValue));
                }
                else
                {
                    (defaultScheduler ? MethodCall() : RunOnScheduler().Unwrap()).LogException(Logger, method.MethodInfo);
                    return objectParameters ? null : Response.Success(request, "");
                }

                Task MethodCall() => method.Invoke(service, arguments);
                Task<Task> RunOnScheduler()
                {
                    Func<Task> action = MethodCall;
                    var parentId = Activity.Current?.Id;

                    if (!string.IsNullOrWhiteSpace(parentId))
                    {
                        action = () =>
                        {
                            using var activity = _activitySource.StartActivity(
                                $"INVOKE {method.MethodInfo.DeclaringType.Name}.{method.MethodInfo.Name}",
                                ActivityKind.Internal,
                                parentId: parentId);
                            return MethodCall();
                        };
                    }

                    return Task.Factory.StartNew(action, cancellationToken, TaskCreationOptions.DenyChildAttach, scheduler);
                }
            }
            object[] GetArguments()
            {
                var parameters = method.Parameters;
                var requestParametersLength = objectParameters ? request.ObjectParameters.Length : request.Parameters.Length;
                if (requestParametersLength > parameters.Length)
                {
                    throw new ArgumentException("Too many parameters for " + method.MethodInfo);
                }
                var allArguments = new object[parameters.Length];
                Deserialize();
                SetOptionalArguments();
                return allArguments;
                void Deserialize()
                {
                    object argument;
                    for (int index = 0; index < requestParametersLength; index++)
                    {
                        var parameterType = parameters[index].ParameterType;
                        if (parameterType == typeof(CancellationToken))
                        {
                            argument = cancellationToken;
                        }
                        else if (parameterType == typeof(Stream))
                        {
                            argument = request.UploadStream;
                        }
                        else
                        {
                            argument = objectParameters ? 
                                Serializer.Deserialize(request.ObjectParameters[index], parameterType) :
                                Serializer.Deserialize(request.Parameters[index], parameterType);
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
                        message.ObjectParameters = objectParameters;
                    }
                    return argument;
                }
                void SetOptionalArguments()
                {
                    for (int index = requestParametersLength; index < parameters.Length; index++)
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
        ValueTask SendResponse(Response response, CancellationToken responseCancellation) => 
            _connectionClosed.IsCancellationRequested ? default : _connection.Send(response, responseCancellation);
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