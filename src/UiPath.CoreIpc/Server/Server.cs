using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    class Server
    {
        private readonly Connection _connection;
        private readonly Lazy<IClient> _client;
        private readonly CancellationTokenSource _connectionClosed = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new ConcurrentDictionary<string, CancellationTokenSource>();

        public Server(ILogger logger, ListenerSettings settings, Connection connection, CancellationToken cancellationToken = default, Lazy<IClient> client = null)
        {
            Settings = settings;
            _connection = connection;
            _client = client ?? new Lazy<IClient>(()=>null);
            Serializer = ServiceProvider.GetRequiredService<ISerializer>();
            Logger = logger;
            connection.RequestReceived += (sender, args) => OnRequestReceived(sender, args).LogException(Logger, nameof(OnRequestReceived));
            connection.CancellationRequestReceived += (sender, args) =>
            {
                if (_requests.TryGetValue(args.RequestId, out var cancellation))
                {
                    cancellation.Cancel();
                }
            };
            connection.Closed += (_, __) =>
            {
                Logger.LogDebug($"{Name} closed.");
                _connectionClosed.Cancel();
            };
            return;
            async Task OnRequestReceived(object sender, RequestReceivedEventsArgs requestReceivedEventsArgs)
            {
                var request = requestReceivedEventsArgs.Request;
                try
                {
                    Logger.LogInformation($"{Name} received request {request}...");
                    var endpointName = request.Endpoint ?? EndpointSettings.Default;
                    if (!Endpoints.TryGetValue(endpointName, out var endpoint))
                    {
                        await OnError(new ArgumentOutOfRangeException(nameof(request.Endpoint), $"{Name} cannot find endpoint {request.Endpoint}..."));
                        return;
                    }
                    Response response;
                    var requestCancellation = new CancellationTokenSource();
                    _requests[request.Id] = requestCancellation;
                    await new[] { cancellationToken, requestCancellation.Token, _connectionClosed.Token }.WithTimeout(request.GetTimeout(Settings.RequestTimeout), async token =>
                    {
                        using (var scope = ServiceProvider.CreateScope())
                        {
                            response = await HandleRequest(endpoint, request, scope, token);
                        }
                        Logger.LogInformation($"{Name} sending response for {request}...");
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
        private async Task<Response> HandleRequest(EndpointSettings endpoint, Request request, IServiceScope scope, CancellationToken cancellationToken)
        {
            var contract = endpoint.Contract;
            var service = endpoint.ServiceInstance ?? scope.ServiceProvider.GetService(contract);
            if (service == null)
            {
                return Response.Fail(request, $"No implementation of interface '{contract.FullName}' found.");
            }
            var method = contract.GetInheritedMethod(request.MethodName);
            if (method == null)
            {
                return Response.Fail(request, $"Method '{request.MethodName}' not found in interface '{contract.FullName}'.");
            }
            if (method.IsGenericMethod)
            {
                return Response.Fail(request, "Generic methods are not supported " + method);
            }
            var arguments = GetArguments(endpoint, method, request, cancellationToken);
            return await InvokeMethod(endpoint, request, service, method, arguments);
        }
        private async Task<Response> InvokeMethod(EndpointSettings endpoint, Request request, object service, MethodInfo method, object[] arguments)
        {
            var hasReturnValue = method.ReturnType != typeof(Task);
            var methodCallTask = Task.Factory.StartNew(MethodCall, default, TaskCreationOptions.DenyChildAttach, endpoint.Scheduler ?? TaskScheduler.Default);
            if (hasReturnValue)
            {
                var methodResult = await methodCallTask;
                await methodResult;
                object returnValue = ((dynamic)methodResult).Result;
                return Response.Success(request, Serializer.Serialize(returnValue));
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
        private object[] GetArguments(EndpointSettings endpoint, MethodInfo method, Request request, CancellationToken cancellationToken)
        {
            var parameters = method.GetParameters();
            if (request.Parameters.Length > parameters.Length)
            {
                throw new ArgumentException("Too many parameters for "+method);
            }
            var allArguments = new object[parameters.Length];
            Deserialize();
            SetOptionalArguments();
            SetWellknownArguments();
            return allArguments;
            void Deserialize()
            {
                for (int index = 0; index < request.Parameters.Length; index++)
                {
                    allArguments[index] = Serializer.Deserialize(request.Parameters[index], parameters[index].ParameterType);
                }
            }
            void SetOptionalArguments()
            {
                for (int index = request.Parameters.Length; index < parameters.Length; index++)
                {
                    allArguments[index] = parameters[index].GetDefaultValue();
                }
            }
            void SetWellknownArguments()
            {
                if (parameters.Length == 0)
                {
                    return;
                }
                int messageIndex;
                if (parameters[parameters.Length - 1].ParameterType == typeof(CancellationToken))
                {
                    allArguments[parameters.Length - 1] = cancellationToken;
                    messageIndex = parameters.Length - 2;
                    if (messageIndex < 0)
                    {
                        return;
                    }
                }
                else
                {
                    messageIndex = parameters.Length - 1;
                }
                if (allArguments[messageIndex] == null && parameters[messageIndex].ParameterType == typeof(Message))
                {
                    allArguments[messageIndex] = new Message();
                }
                if (allArguments[messageIndex] is Message message)
                {
                    message.Endpoint = endpoint;
                    message.Client = _client.Value;
                }
            }
        }
    }
}