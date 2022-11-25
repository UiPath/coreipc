using System.Net.Security;
namespace UiPath.CoreIpc;

using ConnectionFactory = Func<Connection, Connection>;
using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;
using InvokeDelegate = Func<IServiceClient, MethodInfo, object[], object>;

interface IServiceClient : IDisposable
{
    Task<TResult> Invoke<TResult>(MethodInfo method, object[] args);
    Connection Connection { get; }
}

class ServiceClient<TInterface> : IServiceClient, IConnectionKey where TInterface : class
{
    private readonly TimeSpan _requestTimeout;
    private readonly ILogger _logger;
    private readonly ConnectionFactory _connectionFactory;
    private readonly BeforeCallHandler _beforeCall;
    private readonly EndpointSettings _serviceEndpoint;
    private readonly SemaphoreSlim _connectionLock = new(1);
    private Connection _connection;
    private ClientConnection _clientConnection;

    internal ServiceClient(TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, string sslServer = null, BeforeCallHandler beforeCall = null, EndpointSettings serviceEndpoint = null)
    {
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
    public TInterface CreateProxy()
    {
        var proxy = DispatchProxy.Create<TInterface, IpcProxy>();
        (proxy as IpcProxy).ServiceClient = this;
        return proxy;
    }
    
    public override int GetHashCode() => HashCode;

    private void OnNewConnection(Connection connection)
    {
        _connection?.Dispose();
        _connection = connection;
        if (_serviceEndpoint == null)
        {
            return;
        }
        var endpointName = _serviceEndpoint.Name;
        var endPoints = connection.Server?.Endpoints;
        if (endPoints != null)
        {
            if (endPoints.ContainsKey(endpointName))
            {
                throw new InvalidOperationException($"Duplicate callback proxy instance {Name} <{typeof(TInterface).Name}, {endpointName}>. Consider using a singleton callback proxy.");
            }
            endPoints.Add(endpointName, _serviceEndpoint);
            return;
        }
        connection.Logger ??= _logger;
        ConcurrentDictionary<string, EndpointSettings> endpoints = new(){ [endpointName] = _serviceEndpoint };
        ListenerSettings listenerSettings = new(Name) { RequestTimeout = _requestTimeout, ServiceProvider = _serviceEndpoint.ServiceProvider, Endpoints = endpoints };
        connection.SetServer(listenerSettings);
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
            var methodName = method.Name;
            WellKnownArguments();
            var timeoutHelper = new TimeoutHelper(clientTimeout, cancellationToken);
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
                var request = new Request(typeof(TInterface).Name, requestId, methodName, messageTimeout.TotalSeconds)
                {
                    Parameters = args, ResponseType = typeof(TResult)
                };
                if (LogEnabled)
                {
                    Log($"IpcClient calling {methodName} {requestId} {Name}.");
                }
                if (!method.ReturnType.IsGenericType)
                {
                    await _connection.Send(request, token);
                    return default;
                }
                var response = await _connection.RemoteCall(request, token);
                if (LogEnabled)
                {
                    Log($"IpcClient called {methodName} {requestId} {Name}.");
                }
                if (response.Error != null)
                {
                    throw new RemoteException(response.Error);
                }
                return (TResult) response.Data;
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
            void WellKnownArguments()
            {
                if (args is [Message { RequestTimeout: var requestTimeout }, ..] && requestTimeout != TimeSpan.Zero)
                {
                    messageTimeout = requestTimeout;
                    clientTimeout = requestTimeout;
                }
                if (args is [.., CancellationToken token])
                {
                    cancellationToken = token;
                    args[^1] = null;
                }
            }
        }
    }

    private Task<bool> EnsureConnection(CancellationToken cancellationToken)
    {
        if (_connectionFactory != null)
        {
            var externalConnection = _connectionFactory(_connection);
            if (externalConnection != null)
            {
                if (_connection == null)
                {
                    OnNewConnection(externalConnection);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }
        if (_clientConnection?.Connected is true)
        {
            return Task.FromResult(false);
        }
        return Connect(cancellationToken);
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
            Stream network;
            try
            {
                network = await clientConnection.Connect(cancellationToken);
            }
            catch
            {
                clientConnection.Dispose();
                throw;
            }
            var stream = SslServer == null ? network : await AuthenticateAsClient(network);
            OnNewConnection(new(stream, _logger, Name));
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
        if (LogEnabled)
        {
            Log(nameof(ReuseClientConnection) + " " + clientConnection);
        }
        OnNewConnection(clientConnection.Connection);
    }

    public void Log(string message) => _logger.LogInformation(message);

    private void InitializeClientConnection(ClientConnection clientConnection)
    {
        _connection.Listen().LogException(_logger, Name);
        clientConnection.Connection = _connection;
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
        if (disposing && _serviceEndpoint != null)
        {
            _connection?.Server.Endpoints.Remove(_serviceEndpoint.Name);
        }
    }

    public override string ToString() => Name;

    public virtual bool Equals(IConnectionKey other) => SslServer == other.SslServer;

    public virtual ClientConnection CreateClientConnection() => throw new NotImplementedException();
}

public class IpcProxy : DispatchProxy, IDisposable
{
    private static readonly MethodInfo InvokeMethod = typeof(IpcProxy).GetStaticMethod(nameof(GenericInvoke));
    private static readonly ConcurrentDictionary<Type, InvokeDelegate> InvokeByType = new();

    internal IServiceClient ServiceClient { get; set; }

    public Connection Connection => ServiceClient.Connection;

    protected override object Invoke(MethodInfo targetMethod, object[] args) => GetInvoke(targetMethod)(ServiceClient, targetMethod, args);

    public void Dispose() => ServiceClient.Dispose();

    public void CloseConnection() => Connection?.Dispose();

    private static InvokeDelegate GetInvoke(MethodInfo targetMethod) => InvokeByType.GetOrAdd(targetMethod.ReturnType, taskType =>
    {
        var resultType = taskType.IsGenericType ? taskType.GenericTypeArguments[0] : typeof(object);
        return InvokeMethod.MakeGenericDelegate<InvokeDelegate>(resultType);
    });

    private static object GenericInvoke<T>(IServiceClient serviceClient, MethodInfo method, object[] args) => serviceClient.Invoke<T>(method, args);
}