using System.Linq.Expressions;

namespace UiPath.Ipc;

using ListenerFactory = Func<IpcServer, Listener>;

internal abstract class Listener : IAsyncDisposable
{
    private static readonly GenericListenerFactoryCache Cache = new();

    public static Listener Create(IpcServer server)
    {
        var transportType = server.Transport.GetType();
        var listenerFactory = Cache.Get(transportType);
        var listener = listenerFactory(server);
        return listener;
    }

    private readonly Lazy<Task> _disposeTask = null!;
    public readonly ServerTransport Config;
    public readonly IpcServer Server;
    private readonly Lazy<string> _loggerCategory;

    private readonly Lazy<ILogger> _lazyLogger;
    public ILogger Logger => _lazyLogger.Value;

    protected Listener(IpcServer server, ServerTransport config)
    {
        _loggerCategory = new(ComputeLoggerCategory);
        Config = config;
        Server = server;
        _lazyLogger = new(() => server.ServiceProvider.GetService<ILoggerFactory>().OrDefault().CreateLogger(LoggerCategory));
        _disposeTask = new(DisposeCore);
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(_disposeTask.Value);

    protected abstract Task DisposeCore();

    private string LoggerCategory => _loggerCategory.Value;

    private string ComputeLoggerCategory()
    => $"{GetType().Namespace}.{nameof(Listener)}<{ConfigType.Name}[{Config}],..>";

    protected abstract Type ConfigType { get; }
}

internal sealed class Listener<TConfig, TListenerState, TConnectionState> : Listener, IAsyncDisposable
    where TConfig : ServerTransport, IListenerConfig<TConfig, TListenerState, TConnectionState>
    where TListenerState : IAsyncDisposable
    where TConnectionState : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _listeningTask = null!;

    public new readonly TConfig Config;

    public TListenerState State { get; }

    public Listener(IpcServer server, TConfig config) : base(server, config)
    {
        Config = config;
        State = Config.CreateListenerState(server);

        _listeningTask = Task.Run(() => Listen(_cts.Token));
    }

    public void Log(string message)
    {
        if (!Logger.Enabled())
        {
            return;
        }

        Logger.LogInformation(message);
    }
    public void LogError(Exception exception, string message)
    {
        if (!Logger.Enabled(LogLevel.Error))
        {
            return;
        }

        Logger.LogError(exception, message);
    }

    protected override async Task DisposeCore()
    {
        Log($"Stopping listener {Config}...");
        try
        {
            _cts.Cancel();
        }
        catch (Exception ex)
        {
            LogError(ex, $"Canceling {Config} failed.");
        }
        try
        {
            await _listeningTask;
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == _cts.Token)
        {
            Log($"Stopping listener {Config} threw OCE.");
        }
        catch (Exception ex)
        {
            LogError(ex, $"Stopping listener {Config} failed.");
        }
        await State.DisposeAsync();
        _cts.Dispose();
    }

    private async Task Listen(CancellationToken ct)
    {
        Log($"Starting listener {Config}...");

        await Task.WhenAll(Enumerable.Range(1, Config.ConcurrentAccepts).Select(async _ =>
        {
            while (!ct.IsCancellationRequested)
            {
                await AcceptConnection(ct);
            }
        }));
    }
    private async Task AcceptConnection(CancellationToken ct)
    {
        var serverConnection = new ServerConnection<TConfig, TListenerState, TConnectionState>(this);

        Stream? network = null;
        try
        {
            network = await serverConnection.AcceptClient(ct);
        }
        catch
        {
            serverConnection.Dispose();
            return;
        }

        try
        {
            _ = Task.Run(TryToListen);
        }
        catch (Exception ex)
        {
            serverConnection.Dispose();
            if (!ct.IsCancellationRequested)
            {
                Logger.LogException(ex, Config);
            }
        }

        async Task TryToListen()
        {
            try
            {
                await serverConnection.Listen(network, ct);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Listen loop failed for {Config}");
            }
        }
    }

    protected override Type ConfigType => typeof(TConfig);
}

internal sealed class GenericListenerFactoryCache
{
    private readonly ConcurrentDictionary<Type, Result<ListenerFactory>> _cache = new();

    public ListenerFactory Get(Type configType) => _cache.GetOrAdd(configType, Create).Value;

    private Result<ListenerFactory> Create(Type configType)
    {
        try
        {
            return new(Emit(configType));
        }
        catch (Exception ex)
        {
            return new(ex);
        }

        static ListenerFactory Emit(Type configType)
        {
            if (configType.GetInterfaces().SingleOrDefault(IsIListenerConfig) is not { } iface
                || iface.GetGenericArguments() is not [_, var listenerStateType, var connectionStateType])
            {
                throw new ArgumentOutOfRangeException(nameof(iface), $"The ListenerConfig type must implement IListenerConfig<,>. ListenerConfig type was: {configType.FullName}");
            }

            var listenerType = typeof(Listener<,,>).MakeGenericType(configType, listenerStateType, connectionStateType);
            var listenerCtor = listenerType.GetConstructor(
                bindingAttr: BindingFlags.Public | BindingFlags.Instance,
                binder: Type.DefaultBinder,
                types: [typeof(IpcServer), configType],
                modifiers: null)!;

            var paramofServer = Expression.Parameter(typeof(IpcServer));
            var paramofConfig = Expression.Parameter(typeof(ServerTransport));
            var lambda = Expression.Lambda(
                delegateType: typeof(ListenerFactory),
                body: Expression.New(listenerCtor, paramofServer, Expression.Convert(paramofConfig, configType)),
                paramofServer,
                paramofConfig);

            var @delegate = (lambda.Compile() as ListenerFactory)!;
            return @delegate;
        }

        static bool IsIListenerConfig(Type candidateIface)
        => candidateIface.IsGenericType && candidateIface.GetGenericTypeDefinition() == typeof(IListenerConfig<,,>);
    }
}
