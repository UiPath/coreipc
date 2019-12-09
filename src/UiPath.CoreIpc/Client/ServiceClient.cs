using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using static System.Linq.Expressions.Expression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Runtime.ExceptionServices;
using System.Net.Security;
using System.Security.Principal;
using System.Net;
using System.Diagnostics;

namespace UiPath.CoreIpc
{
    using ConnectionFactory = Func<Connection, CancellationToken, Task<Connection>>;
    using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;
    using InvokeAsyncDelegate = Func<IServiceClient, string, object[], object>;

    interface IServiceClient : IDisposable
    {
        Task<TResult> InvokeAsync<TResult>(string methodName, object[] args);
    }

    public class ServiceClient<TInterface> : IServiceClient where TInterface : class
    {
        private readonly ISerializer _serializer;
        private readonly TimeSpan _requestTimeout;
        protected readonly ILogger _logger;
        protected readonly ConnectionFactory _connectionFactory;
        protected readonly bool _encryptAndSign;
        protected readonly BeforeCallHandler _beforeCall;
        protected readonly EndpointSettings _serviceEndpoint;
        private readonly AsyncLock _connectionLock = new AsyncLock();
        protected Connection _connection;
        private Server _server;

        internal ServiceClient(ISerializer serializer, TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, bool encryptAndSign = false, BeforeCallHandler beforeCall = null, EndpointSettings serviceEndpoint = null)
        {
            _serializer = serializer;
            _requestTimeout = requestTimeout;
            _logger = logger;
            _connectionFactory = connectionFactory ?? ((_, __)=>Task.FromResult((Connection)null));
            _encryptAndSign = encryptAndSign;
            _beforeCall = beforeCall ?? ((_, __) => Task.CompletedTask);
            _serviceEndpoint = serviceEndpoint;
        }

        public virtual string Name => _connection?.Name;

        public TInterface CreateProxy()
        {
            var proxy = DispatchProxy.Create<TInterface, InterceptorProxy>();
            (proxy as InterceptorProxy).ServiceClient = this;
            return proxy;
        }

        protected async Task CreateConnection(Stream network, string name)
        {
            var stream = _encryptAndSign ? await AuthenticateAsClient() : network;
            OnNewConnection(new Connection(stream, _serializer, _logger, name));
            _logger?.LogInformation($"CreateConnection {Name}.");
            return;
            async Task<Stream> AuthenticateAsClient()
            {
                var negotiateStream = new NegotiateStream(network);
                try
                {
                    await negotiateStream.AuthenticateAsClientAsync(new NetworkCredential(), "", ProtectionLevel.EncryptAndSign, TokenImpersonationLevel.Identification);
                }
                catch
                {
                    negotiateStream.Dispose();
                    throw;
                }
                Debug.Assert(negotiateStream.IsEncrypted && negotiateStream.IsSigned);
                return negotiateStream;
            }
        }

        protected void OnNewConnection(Connection connection, bool alreadyHasServer = false)
        {
            _connection?.Dispose();
            _connection = connection;
            if (alreadyHasServer || _serviceEndpoint == null)
            {
                return;
            }
            var endpoints = new ConcurrentDictionary<string, EndpointSettings> { [_serviceEndpoint.Name] = _serviceEndpoint };
            var listenerSettings = new ListenerSettings(Name) { RequestTimeout = _requestTimeout, ServiceProvider = _serviceEndpoint.ServiceProvider, Endpoints = endpoints };
            _server = new Server(listenerSettings, connection);
        }

        public async Task<TResult> InvokeAsync<TResult>(string methodName, object[] args)
        {
            var cancellationToken = args.OfType<CancellationToken>().LastOrDefault();
            var messageTimeout = (args.OfType<Message>().FirstOrDefault()?.RequestTimeout.TotalSeconds).GetValueOrDefault();
            var timeout = messageTimeout == 0 ? _requestTimeout : TimeSpan.FromSeconds(messageTimeout);
            return await Task.Run(InvokeAsync).ConfigureAwait(false);
            Task<TResult> InvokeAsync() =>
                cancellationToken.WithTimeout(timeout, async token =>
                {
                    bool newConnection;
                    using (await _connectionLock.LockAsync(token))
                    {
                        newConnection = await EnsureConnection(token);
                    }
                    await _beforeCall(new CallInfo(newConnection), token);
                    var requestId = _connection.NewRequestId();
                    var arguments = args.Select(_serializer.Serialize).ToArray();
                    var request = new Request(typeof(TInterface).Name, requestId, methodName, arguments, messageTimeout);
                    _logger?.LogInformation($"IpcClient calling {methodName} {requestId} {Name}.");
                    var response = await _connection.Send(request, token);
                    _logger?.LogInformation($"IpcClient called {methodName} {requestId} {Name}.");
                    return _serializer.Deserialize<TResult>(response.CheckError().Data ?? "");
                }, methodName, ex =>
                {
                    var exception = ex;
                    if (cancellationToken.IsCancellationRequested && !(ex is TaskCanceledException))
                    {
                        exception = new TaskCanceledException(methodName, ex);
                    }
                    ExceptionDispatchInfo.Capture(exception).Throw();
                    return Task.CompletedTask;
                });
        }

        protected virtual Task<bool> ConnectToServerAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

        protected async Task<bool> EnsureConnection(CancellationToken cancellationToken)
        {
            var externalConnection = await _connectionFactory(_connection, cancellationToken);
            if (externalConnection != null)
            {
                if (_connection == null)
                {
                    OnNewConnection(externalConnection);
                    return true;
                }
                return false;
            }
            return await ConnectToServerAsync(cancellationToken);
        }

        private protected void ReuseClientConnection(ClientConnection clientConnection)
        {
            var alreadyHasServer = clientConnection.Server != null;
            OnNewConnection(clientConnection.Connection, alreadyHasServer);
            if (!alreadyHasServer)
            {
                clientConnection.Server = _server;
            }
            else if (_serviceEndpoint != null)
            {
                _server = clientConnection.Server;
                _server.Endpoints[_serviceEndpoint.Name] = _serviceEndpoint;
            }
        }

        private protected async Task CreateClientConnection(ClientConnection clientConnection, Stream network, string name)
        {
            var serverEndpoints = clientConnection.Server?.Endpoints;
            await CreateConnection(network, name);
            _server?.AddCallbackEndpoints(serverEndpoints);
            _connection.Listen().LogException(_logger, name);
            clientConnection.Connection = _connection;
            clientConnection.Network = network;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _logger?.LogInformation($"Dispose {Name}");
            if (disposing)
            {
                if (_server != null)
                {
                    _server.Endpoints.Remove(_serviceEndpoint.Name);
                }
            }
        }
    }

    public class InterceptorProxy : DispatchProxy, IDisposable
    {
        private static readonly MethodInfo InvokeAsyncMethod = typeof(IServiceClient).GetMethod(nameof(IServiceClient.InvokeAsync));
        private static readonly ConcurrentDictionary<Type, InvokeAsyncDelegate> _invokeAsyncByType = new ConcurrentDictionary<Type, InvokeAsyncDelegate>();

        internal IServiceClient ServiceClient { get; set; }

        protected override object Invoke(MethodInfo targetMethod, object[] args) => GetInvokeAsync(targetMethod.ReturnType)(ServiceClient, targetMethod.Name, args);

        public void Dispose() => ServiceClient.Dispose();

        public void CloseConnection() => ClientConnectionsRegistry.Close((IConnectionKey)ServiceClient);

        private static InvokeAsyncDelegate GetInvokeAsync(Type returnType) => _invokeAsyncByType.GetOrAdd(returnType, CreateDelegate);

        private static InvokeAsyncDelegate CreateDelegate(Type taskType)
        {
            var resultType = taskType.GetGenericArguments().SingleOrDefault() ?? typeof(object);
            var serviceClient = Parameter(typeof(IServiceClient), "serviceClient");
            var methodName = Parameter(typeof(string), "methodName");
            var methodArgs = Parameter(typeof(object[]), "methodArgs");
            var invokeAsyncMethod = InvokeAsyncMethod.MakeGenericMethod(resultType);
            var invokeAsyncCall = Call(serviceClient, invokeAsyncMethod, methodName, methodArgs);
            var lambda = Lambda<InvokeAsyncDelegate>(invokeAsyncCall, serviceClient, methodName, methodArgs);
            return lambda.Compile();
        }

        public static void CloseConnections() => ClientConnectionsRegistry.Clear();
    }
}