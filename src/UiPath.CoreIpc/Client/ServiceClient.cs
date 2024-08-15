namespace UiPath.Ipc;

internal abstract class ServiceClient : IDisposable
{
    #region " NonGeneric-Generic adapter cache "
    private static readonly MethodInfo GenericDefinition = ((Func<ServiceClient, MethodInfo, object?[], Task<int>>)Invoke<int>).Method.GetGenericMethodDefinition();
    private static readonly ConcurrentDictionary<Type, InvokeDelegate> ReturnTypeToInvokeDelegate = new();
    private static InvokeDelegate GetInvokeDelegate(Type returnType) => ReturnTypeToInvokeDelegate.GetOrAdd(returnType, CreateInvokeDelegate);
    private static InvokeDelegate CreateInvokeDelegate(Type returnType)
    => GenericDefinition.MakeGenericDelegate<InvokeDelegate>(
        returnType.IsGenericType
            ? returnType.GetGenericArguments()[0]
            : typeof(object));

    private static Task<TResult> Invoke<TResult>(ServiceClient serviceClient, MethodInfo method, object?[] args) => serviceClient.Invoke<TResult>(method, args);
    #endregion

    protected abstract TimeSpan RequestTimeout { get; }
    protected abstract BeforeCallHandler? BeforeCall { get; }
    protected abstract ILogger? Log { get; }
    protected abstract string DebugName { get; }
    protected abstract ISerializer? Serializer { get; }
    public abstract Stream? Network { get; }

    public event EventHandler? ConnectionClosed;

    private readonly Type _interfaceType;
    private readonly ContextfulLazy<IpcProxy> _proxy = new();

    protected ServiceClient(Type interfaceType)
    {
        _interfaceType = interfaceType;
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

        return (_proxy.GetValue(() =>
        {
            var proxy = (DispatchProxy.Create<T, IpcProxy>() as IpcProxy)!;
            proxy.ServiceClient = this;
            return proxy;
        }) as T)!;
    }

    private Task<TResult> Invoke<TResult>(MethodInfo method, object?[] args)
    {
        var syncContext = SynchronizationContext.Current;
        var defaultContext = syncContext is null || syncContext.GetType() == typeof(SynchronizationContext);
        return defaultContext ? Invoke() : Task.Run(Invoke);

        async Task<TResult> Invoke()
        {
            CancellationToken cancellationToken = default;
            TimeSpan messageTimeout = default;
            TimeSpan clientTimeout = RequestTimeout;
            Stream? uploadStream = null;
            var methodName = method.Name;

            var serializedArguments = SerializeArguments();

            var timeoutHelper = new TimeoutHelper(clientTimeout, cancellationToken);
            try
            {
                var ct = timeoutHelper.Token;

                var (connection, newConnection) = await EnsureConnection(ct);

                if (BeforeCall is not null)
                {
                    var callInfo = new CallInfo(newConnection, method, args);
                    await BeforeCall(callInfo, ct);
                }

                var requestId = connection.NewRequestId();
                var request = new Request(_interfaceType.Name, requestId, methodName, serializedArguments, messageTimeout.TotalSeconds)
                {
                    UploadStream = uploadStream
                };

                Log?.ServiceClientCalling(methodName, requestId, DebugName);
                var response = await connection.RemoteCall(request, ct); // returns user errors instead of throwing them (could throw for system bugs)
                Log?.ServiceClientCalled(methodName, requestId, DebugName);

                return response.Deserialize<TResult>(Serializer);
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

                    result[index] = Serializer.OrDefault().Serialize(args[index]);
                }

                return result;
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    private void Dispose(bool disposing)
    {
        Log?.ServiceClientDispose(DebugName);
    }
    public override string ToString() => DebugName;
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
            if (LatestConnection is not null && _clientState.IsConnected())
            {
                return (LatestConnection, newlyConnected: false);
            }

            LatestConnection = new Connection(await Connect(ct), Serializer, Log, DebugName);
            var router = new Router(_client.Config.CreateCallbackRouterConfig(), _client.Config.ServiceProvider);
            _latestServer = new Server(router, _client.Config.RequestTimeout, LatestConnection);
            LatestConnection.Listen().LogException(Log, DebugName);
            return (LatestConnection, newlyConnected: true);
        }
    }

    private async Task<Network> Connect(CancellationToken ct)
    {
        await _clientState.Connect(_client, ct);

        if (_clientState.Network is not { } network)
        {
            throw new InvalidOperationException();
        }

        return network;
    }

    protected override TimeSpan RequestTimeout => _client.Config.RequestTimeout;
    protected override BeforeCallHandler? BeforeCall => _client.Config.BeforeCall;
    protected override ILogger? Log => _client.Config.Logger;
    protected override string DebugName => _client.Transport.ToString();
    protected override ISerializer? Serializer => _client.Config.Serializer;
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

    protected override Task<(Connection connection, bool newlyConnected)> EnsureConnection(CancellationToken ct)
    => Task.FromResult((_connection, newlyConnected: false));

    protected override TimeSpan RequestTimeout => _listener.Config.RequestTimeout;
    protected override BeforeCallHandler? BeforeCall => null;
    protected override ILogger? Log => null;
    protected override string DebugName => $"ReverseClient for {_listener}";
    protected override ISerializer? Serializer => null;
}
