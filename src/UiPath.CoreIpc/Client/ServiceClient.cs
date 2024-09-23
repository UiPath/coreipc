namespace UiPath.Ipc;

internal abstract class ServiceClient : IDisposable
{
    protected abstract IServiceClientConfig Config { get; }
    protected abstract Telemetry.ServiceClientKind Kind { get; }
    public abstract Stream? Network { get; }
    public event EventHandler? ConnectionClosed;

    private readonly Type _interfaceType;
    private readonly ContextfulLazy<IpcProxy> _proxy = new();

    protected readonly Telemetry.ServiceClientCreated _telemetry;

    protected ServiceClient(Type interfaceType, Telemetry.Id modified, string? callbackServerConfig = null)
    {
        _telemetry = new Telemetry.ServiceClientCreated
        {
            Modified = modified,
            ServiceClientKind = Kind,
            InterfaceTypeName = interfaceType.AssemblyQualifiedName!,
            CallbackServerConfig = callbackServerConfig
        }.Log();

        _interfaceType = interfaceType;
    }

    protected void RaiseConnectionClosed() => ConnectionClosed?.Invoke(this, EventArgs.Empty);
    public virtual ValueTask CloseConnection() => throw new NotSupportedException();
    public object? Invoke(MethodInfo method, object?[] args) => GetInvokeDelegate(method.ReturnType)(this, method, args);

    protected abstract Task<(Connection connection, bool newlyConnected)> EnsureConnection(Telemetry.InvokeRemoteProper telemInvokeRemoteProper, CancellationToken ct);

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

        var telemInvokeRemote = new Telemetry.InvokeRemote
        {
            ServiceClientId = _telemetry.Id,
            Method = method.Name,
            DefaultSynchronizationContext = defaultContext
        }.Log();

        return telemInvokeRemote.Monitor(
            async () =>
            {
                if (defaultContext)
                {
                    return await Invoke();
                }

                return await Task.Run(Invoke);
            });

        async Task<TResult> Invoke()
        {
            CancellationToken cancellationToken = default;
            TimeSpan messageTimeout = default;
            TimeSpan clientTimeout = Config.RequestTimeout;
            Stream? uploadStream = null;
            var methodName = method.Name;

            var serializedArguments = SerializeArguments();

            var timeoutHelper = new TimeoutHelper(clientTimeout, cancellationToken);
            try
            {
                var telemInvokeRemoteProper = new Telemetry.InvokeRemoteProper
                {
                    InvokeRemoteId = telemInvokeRemote.Id,
                    ClientTimeout = clientTimeout,
                    MessageTimeout = messageTimeout,
                    SerializedArgs = serializedArguments,
                };

                return await telemInvokeRemoteProper.Monitor(async () =>
                {
                    var ct = timeoutHelper.Token;

                    var (connection, newConnection) = await EnsureConnection(telemInvokeRemoteProper, ct);

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

                    Config.Logger?.ServiceClientCalling(methodName, requestId, Config.DebugName);

                    Response response;
                    try
                    {
                        response = await connection.RemoteCall(request, ct); // returns user errors instead of throwing them (could throw for system bugs)
                        Config.Logger?.LogInformation($"RemoteCall succeeded. Called method was {request.MethodName}. Response.Error?.Type is {response.Error?.Type ?? "null"}. Response.Data is {response.Data}");
                    }
                    catch (Exception ex)
                    {
                        Config.Logger?.LogError($"RemoteCall failed. Called method was {request.MethodName}. Caught exception is: {ex}");
                        throw;
                    }
                    Config.Logger?.ServiceClientCalled(methodName, requestId, Config.DebugName);

                    return response.Deserialize<TResult>(Config.Serializer);
                });
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

                    result[index] = Config.Serializer.OrDefault().Serialize(args[index]);
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
        new Telemetry.ServiceClientDisposed
        {
            StartId = _telemetry.Id,
        }.Log();

        Config.Logger?.ServiceClientDispose(Config.DebugName);
    }
    public override string ToString() => Config.DebugName;

    #region Generic adapter cache
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
    private Telemetry.Connect? _latestConnectionTelemetry;

    protected override Telemetry.ServiceClientKind Kind => Telemetry.ServiceClientKind.Proper;

    public override Stream? Network => LatestConnection?.Network;

    public ServiceClientProper(IpcClient client, Type interfaceType, Telemetry.IpcClientInitialized telemCause) : base(interfaceType, telemCause.Id)
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
            _latestConnectionTelemetry = null;
        }
    }

    private void LatestConnection_Closed(object? sender, EventArgs e) => RaiseConnectionClosed();

    protected override async Task<(Connection connection, bool newlyConnected)> EnsureConnection(Telemetry.InvokeRemoteProper telemInvokeRemoteProper, CancellationToken ct)
    {
        var telemEnsureConnection = new Telemetry.EnsureConnection
        {
            ServiceClientId = _telemetry.Id.Value,
            InvokeRemoteProper = telemInvokeRemoteProper.Id,
            Config = Config.ToString()!,
            ClientTransport = _client.Transport
        };

        return await telemEnsureConnection.Monitor(
            sanitizeSucceeded: (result, record) => new Telemetry.EnsureConnectionSucceeded
            {
                EnsureConnectionId = record.Id,
                ConnectionDebugName = result.Item1.DebugName,
                NewlyCreated = result.newlyConnected
            },
            asyncFunc: async () =>
            {
                using (await _lock.Lock(ct))
                {
                    var haveConnectionAlready = LatestConnection is not null;
                    var isConnected = new Lazy<bool>(_clientState.IsConnected);
                    var haveBeforeConnect = Config.BeforeConnect is not null;

                    var telemEnsureConnectionInitialState = new Telemetry.EnsureConnectionInitialState
                    {
                        Cause = telemEnsureConnection.Id,
                        HaveConnectionAlready = haveConnectionAlready,
                        IsConnected = isConnected.Value,
                        BeforeConnectIsNotNull = haveBeforeConnect
                    }.Log();

                    if (haveConnectionAlready && isConnected.Value)
                    {
                        return (LatestConnection!, newlyConnected: false);
                    }

                    if (haveBeforeConnect)
                    {
                        await Config.BeforeConnect!(ct);
                    }

                    Stream network = null!;
                    var telemConnect = _latestConnectionTelemetry = new Telemetry.Connect { Cause = telemEnsureConnectionInitialState.Id };
                    await telemConnect.Monitor(
                        async () =>
                        {
                            network = await Connect(ct);
                        });
                    LatestConnection = new Connection(network, Config.Serializer, Config.Logger, Config.DebugName);
                    var router = new Router(_client.Config.CreateCallbackRouterConfig(), _client.Config.ServiceProvider);
                    _latestServer = new Server(router, _client.Config.RequestTimeout, LatestConnection);

                    var telemClientConnectionListen = new Telemetry.ClientConnectionListen { Cause = telemConnect.Id }.Log();

                    _ = Pal();
                    return (LatestConnection, newlyConnected: true);

                    async Task Pal()
                    {
                        try
                        {
                            var telemConnectionListenReason = new Telemetry.ConnectionListenReason { ClientConnectionListenId = telemClientConnectionListen.Id };
                            await telemConnectionListenReason.Monitor(async () => await LatestConnection.Listen(telemConnectionListenReason));
                        }
                        catch (Exception ex)
                        {
                            Config.Logger.LogException(ex, Config.DebugName);
                        }
                    }
                }
            });
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

    protected override Telemetry.ServiceClientKind Kind => Telemetry.ServiceClientKind.Callback;

    public override Stream? Network => _connection.Network;

    public ServiceClientForCallback(Connection connection, Listener listener, Type interfaceType, Telemetry.ServerConnectionCreated telemCause) : base(interfaceType, telemCause.Id, listener.Config.ToString())
    {
        _connection = connection;
        _listener = listener;
    }

    protected override Task<(Connection connection, bool newlyConnected)> EnsureConnection(Telemetry.InvokeRemoteProper telemInvokeRemoteProper, CancellationToken ct)
    => Task.FromResult((_connection, newlyConnected: false));

    protected override IServiceClientConfig Config => _listener.Config;
}
