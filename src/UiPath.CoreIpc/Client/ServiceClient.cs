namespace UiPath.Ipc;

internal abstract class ServiceClient : IDisposable
{
    private static readonly MethodInfo GenericDefOf_CreateProxy = ((Func<ServiceClient, IpcProxy>)CreateProxy<object>).Method.GetGenericMethodDefinition();

    private static IpcProxy CreateProxy<T>(ServiceClient serviceClient) where T : class
    {
        var proxy = (DispatchProxy.Create<T, IpcProxy>() as IpcProxy)!;
        proxy.ServiceClient = serviceClient;
        return proxy;
    }

    protected abstract IServiceClientConfig Config { get; }
    public abstract Stream? Network { get; }
    public event EventHandler? ConnectionClosed;

    private readonly Type _interfaceType;
    private readonly Lazy<IpcProxy> _proxy;

    protected ServiceClient(Type interfaceType)
    {
        _interfaceType = interfaceType;
        _proxy = new(() => (GenericDefOf_CreateProxy.MakeGenericMethod(interfaceType).Invoke(null, [this]) as IpcProxy)!);
    }

    protected void RaiseConnectionClosed() => ConnectionClosed?.Invoke(this, EventArgs.Empty);
    public virtual ValueTask CloseConnection() => throw new NotSupportedException();
    public object? Invoke(MethodInfo method, object?[] args) => GetInvokeDelegate(method.ReturnType)(this, method, args);

    protected abstract Task<(Connection connection, bool newlyConnected)> EnsureConnection(CancellationToken ct);

    public T GetProxy<T>() where T : class
    {
        if (!typeof(T).IsAssignableFrom(_interfaceType))
        {
            throw new ArgumentOutOfRangeException($"The provided generic argument T is not assignable to the proxy type. T is {typeof(T).Name}. The proxy type is {_interfaceType.Name}.");
        }

        return (_proxy.Value as T)!;
    }

    private Task<TResult> Invoke<TResult>(MethodInfo method, object?[] args)
    {
        var sc = SynchronizationContext.Current;
        var defaultContext =
            (sc is null || sc.GetType() == typeof(SynchronizationContext)) &&
            TaskScheduler.Current == TaskScheduler.Default;

        return defaultContext ? Invoke() : Task.Run(Invoke);

        async Task<TResult> Invoke()
        {
            CancellationToken cancellationToken = default;
            TimeSpan messageTimeout = default;
            TimeSpan clientTimeout = Config.RequestTimeout;
            Stream? uploadStream = null;
            var methodName = method.Name;

            var serializedArguments = SerializeArguments();

            using var timeoutHelper = new TimeoutHelper(clientTimeout, cancellationToken);
            try
            {
                var ct = timeoutHelper.Token;

                var (connection, newConnection) = await EnsureConnection(ct);

                if (Config.BeforeCall is not null)
                {
                    var callInfo = new CallInfo(newConnection, method, args);
                    await Config.BeforeCall(callInfo, ct);
                }

                var requestId = connection.NewRequestId();
                var request = new Request(_interfaceType.Name, requestId, methodName, serializedArguments, messageTimeout.TotalSeconds)
                {
                    UploadStream = uploadStream
                };

                Config.Logger?.ServiceClient_Calling(methodName, requestId, Config.DebugName);

                Response response;
                try
                {
                    response = await connection.RemoteCall(request, ct); // returns user errors instead of throwing them (could throw for system bugs)

                    Config.Logger?.ServiceClient_CalledSuccessfully(request.MethodName, requestId, Config.DebugName);
                }
                catch (Exception ex)
                {
                    Config.Logger?.ServiceClient_FailedToCall(request.MethodName, requestId, Config.DebugName, ex);
                    throw;
                }

                return response.Deserialize<TResult>(Config.Serializer);
            }
            catch (Exception ex)
            {
                timeoutHelper.ThrowTimeout(ex, methodName);
                throw;
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

                    result[index] = Config.Serializer.OrDefault().Serialize(args[index]);
                }

                return result;
            }
        }
    }

    public abstract void Dispose();

    public override string ToString() => Config.DebugName;

    #region Generic adapter cache
    private static readonly MethodInfo GenericDefOf_Invoke = ((Func<ServiceClient, MethodInfo, object?[], Task<int>>)Invoke<int>).Method.GetGenericMethodDefinition();
    private static readonly ConcurrentDictionary<Type, InvokeDelegate> ReturnTypeToInvokeDelegate = new();
    private static InvokeDelegate GetInvokeDelegate(Type returnType) => ReturnTypeToInvokeDelegate.GetOrAdd(returnType, CreateInvokeDelegate);
    private static InvokeDelegate CreateInvokeDelegate(Type returnType)
    => GenericDefOf_Invoke.MakeGenericDelegate<InvokeDelegate>(
        returnType.IsGenericType
            ? returnType.GetGenericArguments()[0]
            : typeof(object));

    private static Task<TResult> Invoke<TResult>(ServiceClient serviceClient, MethodInfo method, object?[] args) => serviceClient.Invoke<TResult>(method, args);
    #endregion
}

internal sealed class ServiceClientProper : ServiceClient
{
    private readonly FastAsyncLock _lock = new();
    private readonly IpcClient _client;
    private readonly IClientState _clientState;

    private Connection? _latestConnection;
    private Server? _latestServer;

    private Connection? LatestConnection
    {
        get => _latestConnection;
        set
        {
            if (_latestConnection == value)
            {
                return;
            }

            if (_latestConnection is not null)
            {
                _latestConnection.Closed -= LatestConnection_Closed;
            }

            _latestConnection = value;

            if (_latestConnection is not null)
            {
                _latestConnection.Closed += LatestConnection_Closed;
            }
        }
    }

    public override Stream? Network => LatestConnection?.Network;

    public ServiceClientProper(IpcClient client, Type interfaceType) : base(interfaceType)
    {
        _client = client;
        _clientState = client.Transport.CreateState();
    }

    public override void Dispose()
    {
        CloseConnection().AsTask().TraceError();
    }

    public override async ValueTask CloseConnection()
    {
        using (await _lock.Lock())
        {
            LatestConnection?.Dispose();
            LatestConnection = null;
        }
    }

    private void LatestConnection_Closed(object? sender, EventArgs e) => RaiseConnectionClosed();

    protected override async Task<(Connection connection, bool newlyConnected)> EnsureConnection(CancellationToken ct)
    {

        using (await _lock.Lock(ct))
        {
            var haveConnectionAlready = LatestConnection is not null;
            var isConnected = new Lazy<bool>(_clientState.IsConnected);
            var haveBeforeConnect = Config.BeforeConnect is not null;

            if (haveConnectionAlready && isConnected.Value)
            {
                return (LatestConnection!, newlyConnected: false);
            }

            if (haveBeforeConnect)
            {
                await Config.BeforeConnect!(ct);
            }

            var network = await Connect(ct);

            LatestConnection = new Connection(network, Config.Serializer, Config.Logger, Config.DebugName);
            var router = new Router(_client.Config.CreateCallbackRouterConfig(), _client.Config.ServiceProvider);
            _latestServer = new Server(router, _client.Config.RequestTimeout, LatestConnection);

            _ = Pal();
            return (LatestConnection, newlyConnected: true);

            async Task Pal()
            {
                try
                {
                    await LatestConnection.Listen();
                }
                catch (Exception ex)
                {
                    Config.Logger.LogException(ex, Config.DebugName);
                }
            }
        }
    }

    private async Task<Stream> Connect(CancellationToken ct)
    {
        await _clientState.Connect(_client, ct);

        if (_clientState.Network is not { } network)
        {
            throw new InvalidOperationException();
        }

        return network;
    }

    protected override IServiceClientConfig Config => _client.Config;
}

internal sealed class ServiceClientForCallback : ServiceClient
{
    private readonly Connection _connection;
    private readonly Listener _listener;

    public override Stream? Network => _connection.Network;

    public ServiceClientForCallback(Connection connection, Listener listener, Type interfaceType) : base(interfaceType)
    {
        _connection = connection;
        _listener = listener;
    }

    public override void Dispose()
    {
        // do nothing
    }

    protected override Task<(Connection connection, bool newlyConnected)> EnsureConnection(CancellationToken ct)
    => Task.FromResult((_connection, newlyConnected: false));

    protected override IServiceClientConfig Config => _listener.Config;
}
