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
    using InvokeDelegate = Func<IServiceClient, string, object[], object>;

    interface IServiceClient : IDisposable
    {
        Task<TResult> Invoke<TResult>(string methodName, object[] args);
        Connection Connection { get; }
    }

    class ServiceClient<TInterface> : IServiceClient, IConnectionKey where TInterface : class
    {
        private readonly ISerializer _serializer;
        private readonly TimeSpan _requestTimeout;
        private readonly ILogger _logger;
        private readonly ConnectionFactory _connectionFactory;
        private readonly BeforeCallHandler _beforeCall;
        private readonly EndpointSettings _serviceEndpoint;
        private readonly AsyncLock _connectionLock = new();
        private Connection _connection;
        private Server _server;
        private ClientConnection _clientConnection;

        internal ServiceClient(ISerializer serializer, TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, bool encryptAndSign = false, BeforeCallHandler beforeCall = null, EndpointSettings serviceEndpoint = null)
        {
            _serializer = serializer;
            _requestTimeout = requestTimeout;
            _logger = logger;
            _connectionFactory = connectionFactory;
            EncryptAndSign = encryptAndSign;
            _beforeCall = beforeCall;
            _serviceEndpoint = serviceEndpoint;
        }

        public virtual string Name => _connection?.Name;

        public bool EncryptAndSign { get; }
        
        Connection IServiceClient.Connection => _connection;

        public TInterface CreateProxy()
        {
            var proxy = DispatchProxy.Create<TInterface, IpcProxy>();
            (proxy as IpcProxy).ServiceClient = this;
            return proxy;
        }

        private async Task CreateConnection(Stream network)
        {
            var stream = EncryptAndSign ? await AuthenticateAsClient() : network;
            OnNewConnection(new(stream, _serializer, _logger, Name));
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

        private void OnNewConnection(Connection connection, bool alreadyHasServer = false)
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

        public Task<TResult> Invoke<TResult>(string methodName, object[] args)
        {
            CancellationToken cancellationToken = default;
            TimeSpan messageTimeout = default;
            TimeSpan clientTimeout = _requestTimeout;
            Stream uploadStream = null;
            SetWellKnownArguments();
            return Task.Run(Invoke);
            Task<TResult> Invoke() =>
                cancellationToken.WithTimeout(clientTimeout, async token =>
                {
                    bool newConnection;
                    using (await _connectionLock.LockAsync(token))
                    {
                        newConnection = await EnsureConnection(token);
                    }
                    if (_beforeCall != null)
                    {
                        await _beforeCall(new(newConnection, methodName, args), token);
                    }
                    var requestId = _connection.NewRequestId();
                    var arguments = args.Select(_serializer.Serialize).ToArray();
                    var request = new Request(typeof(TInterface).Name, requestId, methodName, arguments, messageTimeout.TotalSeconds);
                    _logger?.LogInformation($"IpcClient calling {methodName} {requestId} {Name}.");
                    var response = await _connection.RemoteCall(request, uploadStream, token);
                    _logger?.LogInformation($"IpcClient called {methodName} {requestId} {Name}.");
                    if (response.DownloadStream != null)
                    {
                        return (TResult)(object)response.DownloadStream;
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
                            uploadStream = stream;
                            args[index] = "";
                            break;
                    }
                }
            }
        }

        private async Task<bool> EnsureConnection(CancellationToken cancellationToken)
        {
            if (_connectionFactory != null)
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
            }
            return await CheckConnection(cancellationToken);
        }

        private async Task<bool> CheckConnection(CancellationToken cancellationToken)
        {
            if (_clientConnection?.Connected is true)
            {
                return false;
            }
            using var connectionHandle = await ClientConnectionsRegistry.GetOrCreate(this, cancellationToken);
            var clientConnection = connectionHandle.ClientConnection;
            if (clientConnection.Connected)
            {
                ReuseClientConnection(clientConnection);
                return false;
            }
            clientConnection.Dispose();
            try
            {
                await clientConnection.ConnectAsync(cancellationToken);
            }
            catch
            {
                clientConnection.Dispose();
                throw;
            }
            await InitializeClientConnection(clientConnection);
            return true;
        }

        private void ReuseClientConnection(ClientConnection clientConnection)
        {
            _clientConnection = clientConnection;
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

        private async Task InitializeClientConnection(ClientConnection clientConnection)
        {
            await CreateConnection(clientConnection.Network);
            _connection.Listen().LogException(_logger, Name);
            clientConnection.Connection = _connection;
            clientConnection.Server = _server;
            _clientConnection = clientConnection;
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

        public virtual bool Equals(IConnectionKey other) => EncryptAndSign == other.EncryptAndSign;

        public virtual ClientConnection CreateClientConnection(IConnectionKey key) => throw new NotImplementedException();
    }

    public class IpcProxy : DispatchProxy, IDisposable
    {
        private static readonly MethodInfo InvokeMethod = typeof(IServiceClient).GetMethod(nameof(IServiceClient.Invoke));
        private static readonly ConcurrentDictionary<Type, InvokeDelegate> InvokeByType = new();

        internal IServiceClient ServiceClient { get; set; }

        public Connection Connection => ServiceClient.Connection;

        protected override object Invoke(MethodInfo targetMethod, object[] args) => GetInvoke(targetMethod.ReturnType)(ServiceClient, targetMethod.Name, args);

        public void Dispose() => ServiceClient.Dispose();

        public void CloseConnection() => Connection?.Dispose();

        private static InvokeDelegate GetInvoke(Type returnType) => InvokeByType.GetOrAdd(returnType, CreateDelegate);

        private static InvokeDelegate CreateDelegate(Type taskType)
        {
            var resultType = taskType.GetGenericArguments().SingleOrDefault() ?? typeof(object);
            var serviceClient = Parameter(typeof(IServiceClient), "serviceClient");
            var methodName = Parameter(typeof(string), "methodName");
            var methodArgs = Parameter(typeof(object[]), "methodArgs");
            var invokeMethod = InvokeMethod.MakeGenericMethod(resultType);
            var invokeCall = Call(serviceClient, invokeMethod, methodName, methodArgs);
            var lambda = Lambda<InvokeDelegate>(invokeCall, serviceClient, methodName, methodArgs);
            return lambda.Compile();
        }
    }
}