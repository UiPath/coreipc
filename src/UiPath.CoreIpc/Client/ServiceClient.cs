using System.Diagnostics.CodeAnalysis;

namespace UiPath.Ipc;

interface IServiceClient : IDisposable
{
    Task<TResult> Invoke<TResult>(MethodInfo method, object?[] args);

    Connection? Connection { get; }
}

class ServiceClient<TInterface> : IServiceClient where TInterface : class
{
    private readonly ConnectionKey? _key;
    private readonly ConnectionConfig _config;

    private readonly SemaphoreSlim _connectionLock = new(initialCount: 1);

    internal Server? _server;
    private Connection? _connection;
    private ClientConnection? _clientConnection;

    protected int HashCode { get; init; }
    public virtual string DebugName => _connection?.DebugName ?? "";
    private bool LogEnabled => _config.Logger.Enabled();
    Connection? IServiceClient.Connection => _connection;

    internal ServiceClient(ConnectionConfig config, ConnectionKey? key = null)
    {
        _config = config;
        _key = key;
    }

    public TInterface CreateProxy()
    {
        var proxy = DispatchProxy.Create<TInterface, IpcProxy>();
        (proxy as IpcProxy)!.ServiceClient = this;
        return proxy;
    }

    public override int GetHashCode() => HashCode;

    [MemberNotNull(nameof(_connection))]
    private void OnNewConnection(Connection connection, bool alreadyHasServer = false)
    {
        _connection?.Dispose();
        _connection = connection;

        if (alreadyHasServer)
        {
            return;
        }

        connection.Logger ??= _config.Logger;

        _server = new Server(
            new Router(_config.CreateCallbackRouterConfig(), _config.ServiceProvider), 
            _config.RequestTimeout, connection);
    }

    public Task<TResult> Invoke<TResult>(MethodInfo method, object?[] args)
    {
        var syncContext = SynchronizationContext.Current;
        var defaultContext = syncContext == null || syncContext.GetType() == typeof(SynchronizationContext);
        return defaultContext ? Invoke() : Task.Run(Invoke);

        async Task<TResult> Invoke()
        {
            CancellationToken cancellationToken = default;
            TimeSpan messageTimeout = default;
            TimeSpan clientTimeout = _config.RequestTimeout;
            Stream? uploadStream = null;            
            var methodName = method.Name;

            var serializedArguments = SerializeArguments();

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
                if (_config.BeforeCall is not null)
                {
                    await _config.BeforeCall(new CallInfo(newConnection, method, args), token);
                }
                var requestId = _connection.NewRequestId();
                var request = new Request(typeof(TInterface).Name, requestId, methodName, serializedArguments, messageTimeout.TotalSeconds)
                {
                    UploadStream = uploadStream
                };
                if (LogEnabled)
                {
                    Log($"IpcClient calling {methodName} {requestId} {DebugName}.");
                }
                var response = await _connection.RemoteCall(request, token);
                if (LogEnabled)
                {
                    Log($"IpcClient called {methodName} {requestId} {DebugName}.");
                }
                return response.Deserialize<TResult>(_config.Serializer);
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
            
            string[] SerializeArguments()
            {
                var result = new string[args.Length];

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

                    result[index] = _config.Serializer.OrDefault().Serialize(args[index]);
                }

                return result;
            }
        }
    }

    [MemberNotNull(nameof(_connection))]
    private async Task<bool> EnsureConnection(CancellationToken cancellationToken)
    {
        if (_config.ConnectionFactory is not null)
        {
            var userConnection = await _config.ConnectionFactory(_connection, cancellationToken);
            if (userConnection is not null)
            {
                if (_connection is null)
                {
                    OnNewConnection(userConnection);
                    return true;
                }
                return false;
            }
        }

        if (_clientConnection is { Connected: true })
        {
            return false;
        }

        return await Connect(cancellationToken);
    }

    [MemberNotNull(nameof(_clientConnection))]
    private async Task<bool> Connect(CancellationToken cancellationToken)
    {
        if (_key is null)
        {
            throw new InvalidOperationException();
        }

        var clientConnection = await ClientConnectionsRegistry.GetOrCreate(_key, cancellationToken);
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
            OnNewConnection(new Connection(network, _config.Serializer, _config.Logger, DebugName));
            if (LogEnabled)
            {
                Log($"CreateConnection {DebugName}.");
            }

            _connection.Listen().LogException(_config.Logger, DebugName);
            clientConnection.Initialize(_connection, _server);
            _clientConnection = clientConnection;
        }
        finally
        {
            clientConnection.Release();
        }
        return true;
    }

    [MemberNotNull(nameof(_clientConnection))]
    private void ReuseClientConnection(ClientConnection clientConnection)
    {
        _clientConnection = clientConnection;
        var alreadyHasServer = clientConnection.Server is not null;
        if (LogEnabled)
        {
            Log(nameof(ReuseClientConnection) + " " + clientConnection);
        }
        OnNewConnection(clientConnection.Connection!, alreadyHasServer);
        if (!alreadyHasServer)
        {
            clientConnection.Initialize(clientConnection.Connection, _server);            
        }
        else
        {
            _server = clientConnection.Server;
        }
    }

    public void Log(string message) => _config.Logger.OrDefault().LogInformation(message);

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
            Log($"Dispose {DebugName}");
        }
    }

    public override string ToString() => DebugName;
}

public class IpcProxy : DispatchProxy, IDisposable
{
    static IpcProxy()
    {
        var prototype = GenericInvoke<object>;
        InvokeMethod = prototype.Method.GetGenericMethodDefinition();
    }

    private static readonly MethodInfo InvokeMethod;
    private static readonly ConcurrentDictionary<Type, InvokeDelegate> InvokeByType = new();

    internal IServiceClient ServiceClient { get; set; } = null!;

    public Connection? Connection => ServiceClient.Connection;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => GetInvoke(targetMethod!)(ServiceClient, targetMethod!, args!);

    public void Dispose() => ServiceClient?.Dispose();

    public void CloseConnection() => Connection?.Dispose();

    private static InvokeDelegate GetInvoke(MethodInfo targetMethod) => InvokeByType.GetOrAdd(targetMethod.ReturnType, taskType =>
    {
        var resultType = taskType.IsGenericType ? taskType.GenericTypeArguments[0] : typeof(object);
        return InvokeMethod.MakeGenericDelegate<InvokeDelegate>(resultType);
    });

    private static object? GenericInvoke<T>(IServiceClient serviceClient, MethodInfo method, object?[] args) => serviceClient.Invoke<T>(method, args);
}
