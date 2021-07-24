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
        protected readonly TimeSpan _requestTimeout;
        protected readonly ILogger _logger;
        protected readonly ConnectionFactory _connectionFactory;
        protected readonly BeforeCallHandler _beforeCall;
        protected readonly EndpointSettings _serviceEndpoint;
        private readonly AsyncLock _connectionLock = new();
        protected Connection _connection;
        private Server _server;

        internal ServiceClient(ISerializer serializer, TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, bool encryptAndSign = false, BeforeCallHandler beforeCall = null, EndpointSettings serviceEndpoint = null)
        {
            _serializer = serializer;
            _requestTimeout = requestTimeout;
            _logger = logger;
            _connectionFactory = connectionFactory ?? ((_, __)=>Task.FromResult((Connection)null));
            EncryptAndSign = encryptAndSign;
            _beforeCall = beforeCall ?? ((_, __) => Task.CompletedTask);
            _serviceEndpoint = serviceEndpoint;
        }

        public virtual string Name => _connection?.Name;

        public bool EncryptAndSign { get; }

        public TInterface CreateProxy()
        {
            var proxy = DispatchProxy.Create<TInterface, IpcProxy>();
            (proxy as IpcProxy).ServiceClient = this;
            return proxy;
        }

        protected async Task CreateConnection(Stream network, string name)
        {
            var stream = EncryptAndSign ? await AuthenticateAsClient() : network;
            OnNewConnection(new(stream, _serializer, _logger, name));
            _logger?.LogInformation($"CreateConnection {Name}.");
            return;
            async Task<Stream> AuthenticateAsClient()
            {
                var negotiateStream = new NegotiateStream(network);
                try
                {
                    await negotiateStream.AuthenticateAsClientAsync(new(), "", ProtectionLevel.EncryptAndSign, TokenImpersonationLevel.Identification);
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
            _server = new(_logger, listenerSettings, connection);
        }

        public async Task<TResult> InvokeAsync<TResult>(string methodName, object[] args)
        {
            CancellationToken cancellationToken = default;
            TimeSpan messageTimeout = default;
            TimeSpan clientTimeout = _requestTimeout;
            Stream userStream = null;
            SetWellKnownArguments();
            return await Task.Run(InvokeAsync).ConfigureAwait(false);
            Task<TResult> InvokeAsync() =>
                cancellationToken.WithTimeout(clientTimeout, async token =>
                {
                    bool newConnection;
                    using (await _connectionLock.LockAsync(token))
                    {
                        newConnection = await EnsureConnection(token);
                    }
                    await _beforeCall(new(newConnection, methodName, args), token);
                    var requestId = _connection.NewRequestId();
                    var arguments = args.Select(_serializer.Serialize).ToArray();
                    var request = new Request(typeof(TInterface).Name, requestId, methodName, arguments, messageTimeout.TotalSeconds);
                    _logger?.LogInformation($"IpcClient calling {methodName} {requestId} {Name}.");
                    var response = await _connection.Send(request, userStream, token);
                    _logger?.LogInformation($"IpcClient called {methodName} {requestId} {Name}.");
                    if (response.UserStream != null)
                    {
                        return (TResult)(object)response.UserStream;
                    }
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
            void SetWellKnownArguments()
            {
                for(int index = 0; index < args.Length; index++)
                {
                    switch (args[index])
                    {
                        case Message { RequestTimeout: var requestTimeout } when requestTimeout != TimeSpan.Zero:
                            messageTimeout = requestTimeout;
                            clientTimeout = requestTimeout;
                            break;
                        case CancellationToken token:
                            cancellationToken = token;
                            args[index] = "";
                            break;
                        case Stream stream:
                            userStream = stream;
                            args[index] = "";
                            break;
                    }
                }
            }
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
            _logger?.LogInformation(nameof(ReuseClientConnection)+" "+clientConnection);
            OnNewConnection(clientConnection.Connection, alreadyHasServer);
            if (!alreadyHasServer)
            {
                clientConnection.Server = _server;
            }
            else if (_serviceEndpoint != null)
            {
                _server = clientConnection.Server;
                try
                {
                    _server.Endpoints.Add(_serviceEndpoint.Name, _serviceEndpoint);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException($"Duplicate callback proxy instance {Name} <{typeof(TInterface).Name}, {_serviceEndpoint.Contract.Name}>. Consider using a singleton callback proxy.", ex);
                }
            }
        }

        private protected async Task CreateClientConnection(ClientConnection clientConnection, Stream network, object state, string name)
        {
            await CreateConnection(network, name);
            _connection.Listen().LogException(_logger, name);
            clientConnection.Connection = _connection;
            clientConnection.State = state;
            clientConnection.Server = _server;
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
                _server?.Endpoints.Remove(_serviceEndpoint.Name);
            }
        }
    }

    public class IpcProxy : DispatchProxy, IDisposable
    {
        private static readonly MethodInfo InvokeAsyncMethod = typeof(IServiceClient).GetMethod(nameof(IServiceClient.InvokeAsync));
        private static readonly ConcurrentDictionary<Type, InvokeAsyncDelegate> _invokeAsyncByType = new();

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