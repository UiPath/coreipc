namespace UiPath.Ipc;

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
    private readonly SemaphoreSlim _connectionLock = new(1);
    private Connection _connection;
    internal Server? _server;
    private ClientConnection _clientConnection;

    internal ServiceClient(ISerializer serializer, TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, BeforeCallHandler beforeCall = null)
    {
        _serializer = serializer;
        _requestTimeout = requestTimeout;
        _logger = logger;
        _connectionFactory = connectionFactory;
        _beforeCall = beforeCall;
    }
    protected int HashCode { get; init; }
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

    private void OnNewConnection(Connection connection, bool alreadyHasServer = false)
    {
        _connection?.Dispose();
        _connection = connection;
        if (alreadyHasServer)
        {
            return;
        }

        connection.Logger ??= _logger;

        _server = new Server(
            Router.Callbacks,
            settings: new()
            {
                Name = Name,
                RequestTimeout = _requestTimeout,
            },
            connection);
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
                var request = new Request(typeof(TInterface).Name, requestId, methodName, serializedArguments, messageTimeout.TotalSeconds)
                {
                    UploadStream = uploadStream
                };
                if (LogEnabled)
                {
                    Log($"IpcClient calling {methodName} {requestId} {Name}.");
                }
                var response = await _connection.RemoteCall(request, token);
                if (LogEnabled)
                {
                    Log($"IpcClient called {methodName} {requestId} {Name}.");
                }
                return response.Deserialize<TResult>(_serializer);
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
                serializedArguments = new string[args.Length];

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

                    serializedArguments[index] = _serializer.Serialize(args[index]);
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
            OnNewConnection(new(network, _serializer, _logger, Name));
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
    }

    private void ReuseClientConnection(ClientConnection clientConnection)
    {
        _clientConnection = clientConnection;
        var alreadyHasServer = clientConnection.Server is not null;
        if (LogEnabled)
        {
            Log(nameof(ReuseClientConnection) + " " + clientConnection);
        }
        OnNewConnection(clientConnection.Connection, alreadyHasServer);
        if (!alreadyHasServer)
        {
            clientConnection.Server = _server;
        }
        else
        {
            _server = clientConnection.Server;
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
    }

    public override string ToString() => Name;

    public virtual bool Equals(IConnectionKey other) => true;

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

public static class Callback
{
    private static readonly ConcurrentDictionary<string, CallbackRegistration> Registrations = new();

    internal static bool TryResolveRoute(string endpointName, out Route route)
    {
        if (!Registrations.TryGetValue(endpointName, out var registration))
        {
            route = default;
            return false;
        }

        route = Route.From(registration);
        return true;
    }

    public static void Set<TCallback>(IServiceProvider serviceProvider, TaskScheduler? taskScheduler = null, ILoggerFactory? loggerFactory = null, ISerializer? serializer = null) where TCallback : class
    => Set(
        typeof(TCallback),
        service: new ServiceFactory.Injected()
        {
            ServiceProvider = serviceProvider,
            Type = typeof(TCallback)
        },
        taskScheduler, loggerFactory, serializer);
    public static void Set<TCallback>(TCallback instance, TaskScheduler? taskScheduler = null, ILoggerFactory? loggerFactory = null, ISerializer? serializer = null) where TCallback : class
    => Set(
        typeof(TCallback),
        service: new ServiceFactory.Instance()
        {
            ServiceInstance = instance,
            Type = typeof(TCallback)
        },
        taskScheduler, loggerFactory, serializer);
    public static void Set(Type callbackType, IServiceProvider serviceProvider, TaskScheduler? taskScheduler = null, ILoggerFactory? loggerFactory = null, ISerializer? serializer = null)
    => Set(
        callbackType,
        service: new ServiceFactory.Injected()
        {
            ServiceProvider = serviceProvider,
            Type = callbackType
        },
        taskScheduler, loggerFactory, serializer);

    private static void Set(Type callbackType, ServiceFactory service, TaskScheduler? taskScheduler = null, ILoggerFactory? loggerFactory = null, ISerializer? serializer = null)
    {
        var cr = new CallbackRegistration(callbackType, service, taskScheduler, loggerFactory, serializer);
        Registrations[callbackType.Name] = cr;
    }

    internal static bool TryGet(Type callbackType, out CallbackRegistration callbackRegistration)
    => Registrations.TryGetValue(callbackType.Name, out callbackRegistration);

    internal readonly record struct CallbackRegistration(
        Type Type, 
        ServiceFactory Service, 
        TaskScheduler? Scheduler, 
        ILoggerFactory? Logger, 
        ISerializer? Serializer);
}