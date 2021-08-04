using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    class Server
    {
        private readonly Connection _connection;
        private readonly IClient _client;
        private readonly CancellationTokenSource _connectionClosed = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new();

        public Server(ILogger logger, ListenerSettings settings, Connection connection, IClient client = null, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            _connection = connection;
            _client = client;
            Serializer = ServiceProvider.GetRequiredService<ISerializer>();
            Logger = logger;
            connection.RequestReceived += OnRequestReceived;
            connection.CancellationRequestReceived += requestId =>
            {
                if (_requests.TryGetValue(requestId, out var cancellation))
                {
                    cancellation.Cancel();
                }
            };
            connection.Closed += delegate
            {
                Logger.LogDebug($"{Name} closed.");
                _connectionClosed.Cancel();
            };
            return;
            async Task OnRequestReceived(Request request, Stream uploadStream)
            {
                try
                {
                    Logger.LogInformation($"{Name} received request {request}");
                    if (!Endpoints.TryGetValue(request.Endpoint, out var endpoint))
                    {
                        await OnError(new ArgumentOutOfRangeException(nameof(request.Endpoint), $"{Name} cannot find endpoint {request.Endpoint}"));
                        return;
                    }
                    Response response;
                    var requestCancellation = new CancellationTokenSource();
                    _requests[request.Id] = requestCancellation;
                    var timeout = request.GetTimeout(Settings.RequestTimeout);
                    await new[] { cancellationToken, requestCancellation.Token, _connectionClosed.Token }.WithTimeout(timeout, async token =>
                    {
                        using (var scope = ServiceProvider.CreateScope())
                        {
                            response = await HandleRequest(endpoint, scope, token);
                        }
                        Logger.LogInformation($"{Name} sending response for {request}");
                        await SendResponse(response, token);
                    }, request.MethodName, OnError);
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
                return;
                async Task<Response> HandleRequest(EndpointSettings endpoint, IServiceScope scope, CancellationToken cancellationToken)
                {
                    var contract = endpoint.Contract;
                    var service = endpoint.ServiceInstance ?? scope.ServiceProvider.GetService(contract);
                    if (service == null)
                    {
                        return Response.Fail(request, $"No implementation of interface '{contract.FullName}' found.");
                    }
                    var method = contract.GetInterfaceMethod(request.MethodName);
                    if (method == null)
                    {
                        return Response.Fail(request, $"Method '{request.MethodName}' not found in interface '{contract.FullName}'.");
                    }
                    if (method.IsGenericMethod)
                    {
                        return Response.Fail(request, "Generic methods are not supported " + method);
                    }
                    var arguments = GetArguments();
                    var beforeCall = endpoint.BeforeCall;
                    if (beforeCall != null)
                    {
                        await beforeCall(new(default, request.MethodName, arguments), cancellationToken);
                    }
                    return await InvokeMethod();
                    async Task<Response> InvokeMethod()
                    {
                        var hasReturnValue = method.ReturnType != typeof(Task);
                        var methodCallTask = Task.Factory.StartNew(MethodCall, cancellationToken, TaskCreationOptions.DenyChildAttach, endpoint.Scheduler ?? TaskScheduler.Default);
                        if (hasReturnValue)
                        {
                            var methodResult = await methodCallTask;
                            await methodResult;
                            object returnValue = ((dynamic)methodResult).Result;
                            return returnValue is Stream donloadStream ? Response.Success(request, donloadStream) : Response.Success(request, Serializer.Serialize(returnValue));
        }
                        else
                        {
                            methodCallTask.Unwrap().LogException(Logger, method);
                            return Response.Success(request, "");
                        }
                        Task MethodCall()
                        {
                            Logger.LogDebug($"Processing {method.Name} on {Thread.CurrentThread.Name}.");
                            return (Task)method.Invoke(service, arguments);
                        }
                    }
                    object[] GetArguments()
                    {
                        var parameters = method.GetParameters();
                        if (request.Parameters.Length > parameters.Length)
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
                            for (int index = 0; index < request.Parameters.Length; index++)
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
                                    argument = Serializer.Deserialize(request.Parameters[index], parameterType);
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
                                message.Endpoint = endpoint;
                                message.Client = _client;
                            }
                            return argument;
                        }
                        void SetOptionalArguments()
                        {
                            for (int index = request.Parameters.Length; index < parameters.Length; index++)
                            {
                                var parameter = parameters[index];
                                allArguments[index] = CheckMessage(parameter.GetDefaultValue(), parameter.ParameterType);
                            }
                        }
                    }
                }
                Task OnError(Exception ex)
                {
                    Logger.LogException(ex, $"{Name} {request}");
                    return SendResponse(Response.Fail(request, ex), cancellationToken);
                }
            }
        }
        private ILogger Logger { get; }
        private ListenerSettings Settings { get; }
        public IServiceProvider ServiceProvider => Settings.ServiceProvider;
        public ISerializer Serializer { get; }
        public string Name => _connection.Name;
        public IDictionary<string, EndpointSettings> Endpoints => Settings.Endpoints;
        async Task SendResponse(Response response, CancellationToken responseCancellation)
        {
            if (_connectionClosed.IsCancellationRequested)
            {
                return;
            }
            await _connection.Send(response, responseCancellation);
        }
    }
}