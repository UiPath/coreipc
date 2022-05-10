using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Diagnostics;
namespace UiPath.CoreIpc
{
    using ConnectionFactory = Func<Connection, CancellationToken, Task<Connection>>;
    using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;
    using InvokeDelegate = Func<IServiceClient, MethodInfo, object[], object>;

    interface IServiceClient : IDisposable
    {
        Task<TResult> Invoke<TResult>(MethodInfo method, object[] args);
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
        private readonly SemaphoreSlim _connectionLock = new(1);
        private Connection _connection;
        private Server _server;
        private ClientConnection _clientConnection;

        internal ServiceClient(ISerializer serializer, TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, string sslServer = null, BeforeCallHandler beforeCall = null, EndpointSettings serviceEndpoint = null)
        {
            _serializer = serializer;
            _requestTimeout = requestTimeout;
            _logger = logger;
            _connectionFactory = connectionFactory;
            SslServer = sslServer;
            _beforeCall = beforeCall;
            _serviceEndpoint = serviceEndpoint;
        }
        protected int HashCode { get; init; }
        public string SslServer { get; init; }
        public virtual string Name => _connection?.Name;
        private bool LogEnabled => _logger.Enabled();
        Connection IServiceClient.Connection => _connection;
        public bool ObjectParameters { get; init; } = true;

        public TInterface CreateProxy()
        {
            var proxy = DispatchProxy.Create<TInterface, IpcProxy>();
            (proxy as IpcProxy).ServiceClient = this;
            return proxy;
        }
        
        public override int GetHashCode() => HashCode;

        private void OnNewConnection(Connection connection, bool alreadyHasServer = false)
        {
            _connection?.Dispose();
            _connection = connection;
            if (alreadyHasServer || _serviceEndpoint == null)
            {
                return;
            }
            connection.Logger ??= _logger;
            var endpoints = new ConcurrentDictionary<string, EndpointSettings> { [_serviceEndpoint.Name] = _serviceEndpoint };
            var listenerSettings = new ListenerSettings(Name) { RequestTimeout = _requestTimeout, ServiceProvider = _serviceEndpoint.ServiceProvider, Endpoints = endpoints };
            _server = new(listenerSettings, connection);
        }

        public Task<TResult> Invoke<TResult>(MethodInfo method, object[] args)
        {
            var syncContext = SynchronizationContext.Current;
            var defaultContext = syncContext == null || syncContext.GetType() == typeof(SynchronizationContext);
            return defaultContext ? Invoke() : Task.Run(Invoke);
            async Task<TResult> Invoke()
            {
                CancellationToken cancellationToken = default;
                TimeSpan messageTimeout = default;
                TimeSpan clientTimeout = _requestTimeout;
                Stream uploadStream = null;
                string[] serializedArguments = null;
                var methodName = method.Name;
                SerializeArguments();
                var timeoutHelper = TimeoutHelper.Creaate(clientTimeout, cancellationToken);
                try
                {
                    var token = timeoutHelper.Token;
                    bool newConnection;
                    await _connectionLock.WaitAsync(token);
                    try
                    {
                        newConnection = await EnsureConnection(token);
                    }
                    finally
                    {
                        _connectionLock.Release();
                    }
                    if (_beforeCall != null)
                    {
                        await _beforeCall(new(newConnection, method, args), token);
                    }
                    var requestId = _connection.NewRequestId();
                    var request = new Request(typeof(TInterface).Name, requestId, methodName, serializedArguments, ObjectParameters ? args : null, messageTimeout.TotalSeconds, Activity.Current?.Id)
                    {
                        UploadStream = uploadStream
                    };
                    if (LogEnabled)
                    {
                        Log($"IpcClient calling {methodName} {requestId} {Name}.");
                    }
                    if (ObjectParameters && !method.ReturnType.IsGenericType)
                    {
                        await _connection.Send(request, token);
                        return default;
                    }
                    var response = await _connection.RemoteCall(request, token);
                    if (LogEnabled)
                    {
                        Log($"IpcClient called {methodName} {requestId} {Name}.");
                    }
                    return response.Deserialize<TResult>(_serializer, ObjectParameters);
                }
                catch (Exception ex)
                {
                    timeoutHelper.ThrowTimeout(ex, methodName);
                    throw;
                }
                finally
                {
                    timeoutHelper.Dispose();
                }
                void SerializeArguments()
                {
                    if (!ObjectParameters)
                    {
                        serializedArguments = new string[args.Length];
                    }
                    for (int index = 0; index < args.Length; index++)
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
                        if (!ObjectParameters)
                        {
                            serializedArguments[index] = _serializer.Serialize(args[index]);
                        }
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
            if (_clientConnection?.Connected is true)
            {
                return false;
            }
            return await Connect(cancellationToken);
        }

        private async Task<bool> Connect(CancellationToken cancellationToken)
        {
            var clientConnection = await ClientConnectionsRegistry.GetOrCreate(this, cancellationToken);
            try
            {
                if (clientConnection.Connected)
                {
                    ReuseClientConnection(clientConnection);
                    return false;
                }
                clientConnection.Dispose();
                try
                {
                    await clientConnection.Connect(cancellationToken);
                }
                catch
                {
                    clientConnection.Dispose();
                    throw;
                }
                var stream = SslServer == null ? clientConnection.Network : await AuthenticateAsClient(clientConnection.Network);
                OnNewConnection(new(stream, _serializer, _logger, Name));
                if (LogEnabled)
                {
                    Log($"CreateConnection {Name}.");
                }
                InitializeClientConnection(clientConnection);
            }
            finally
            {
                clientConnection.Release();
            }
            return true;
            async Task<Stream> AuthenticateAsClient(Stream network)
            {
                var sslStream = new SslStream(network);
                try
                {
                    await sslStream.AuthenticateAsClientAsync(SslServer);
                }
                catch
                {
                    sslStream.Dispose();
                    throw;
                }
                Debug.Assert(sslStream.IsEncrypted && sslStream.IsSigned);
                return sslStream;
            }
        }

        private void ReuseClientConnection(ClientConnection clientConnection)
        {
            _clientConnection = clientConnection;
            var alreadyHasServer = clientConnection.Server != null;
            if (LogEnabled)
            {
                Log(nameof(ReuseClientConnection) + " " + clientConnection);
            }
            OnNewConnection(clientConnection.Connection, alreadyHasServer);
            if (!alreadyHasServer)
            {
                clientConnection.Server = _server;
            }
            else if (_serviceEndpoint != null)
            {
                _server = clientConnection.Server;
                if (_server.Endpoints.ContainsKey(_serviceEndpoint.Name))
                {
                    throw new InvalidOperationException($"Duplicate callback proxy instance {Name} <{typeof(TInterface).Name}, {_serviceEndpoint.Contract.Name}>. Consider using a singleton callback proxy.");
                }
                _server.Endpoints.Add(_serviceEndpoint.Name, _serviceEndpoint);
            }
        }

        public void Log(string message) => _logger.LogInformation(message);

        private void InitializeClientConnection(ClientConnection clientConnection)
        {
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
            _connectionLock.AssertDisposed();
            if (LogEnabled)
            {
                Log($"Dispose {Name}");
            }
            if (disposing)
            {
                _server?.Endpoints.Remove(_serviceEndpoint.Name);
            }
        }

        public override string ToString() => Name;

        public virtual bool Equals(IConnectionKey other) => SslServer == other.SslServer;

        public virtual ClientConnection CreateClientConnection(IConnectionKey key) => throw new NotImplementedException();
    }

    public class IpcProxy : DispatchProxy, IDisposable
    {
        private static readonly MethodInfo InvokeMethod = typeof(IpcProxy).GetStaticMethod(nameof(GenericInvoke));
        private static readonly ConcurrentDictionaryWrapper<Type, InvokeDelegate> InvokeByType = new(CreateDelegate);

        internal IServiceClient ServiceClient { get; set; }

        public Connection Connection => ServiceClient.Connection;

        protected override object Invoke(MethodInfo targetMethod, object[] args) => GetInvoke(targetMethod)(ServiceClient, targetMethod, args);

        public void Dispose() => ServiceClient.Dispose();

        public void CloseConnection() => Connection?.Dispose();

        private static InvokeDelegate GetInvoke(MethodInfo targetMethod) => InvokeByType.GetOrAdd(targetMethod.ReturnType);

        private static InvokeDelegate CreateDelegate(Type taskType)
        {
            var resultType = taskType.IsGenericType ? taskType.GenericTypeArguments[0] : typeof(object);
            return InvokeMethod.MakeGenericDelegate<InvokeDelegate>(resultType);
        }
        private static object GenericInvoke<T>(IServiceClient serviceClient, MethodInfo method, object[] args) => serviceClient.Invoke<T>(method, args);
    }
}