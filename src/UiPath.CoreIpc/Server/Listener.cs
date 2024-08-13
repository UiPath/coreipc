namespace UiPath.Ipc;

using System.Linq.Expressions;
using ListenerFactory = Func<IpcServer, ListenerConfig, Listener>;

internal abstract class Listener : IAsyncDisposable
{
    public static Listener Create(IpcServer server, ListenerConfig config)
    => GetFactory(config.GetType())(
        server ?? throw new ArgumentNullException(nameof(server)),
        config ?? throw new ArgumentNullException(nameof(config)));

    private readonly struct Result<T>
    {
        private readonly T _value;
        private readonly Exception? _exception;

        public T Value => _exception is null ? _value : throw _exception;

        public Result(T value)
        {
            _value = value;
            _exception = null;
        }

        public Result(Exception exception)
        {
            _value = default!;
            _exception = exception;
        }
    }
    private static readonly ConcurrentDictionary<Type, Result<ListenerFactory>> Factories = new();
    private static ListenerFactory GetFactory(Type configType) => Factories.GetOrAdd(configType, CreateFactory).Value;
    private static Result<ListenerFactory> CreateFactory(Type configType)
    {
        try
        {
            return new(Pal(configType));
        }
        catch (Exception ex)
        {
            return new(ex);
        }

        static ListenerFactory Pal(Type configType)
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
            var paramofConfig = Expression.Parameter(typeof(ListenerConfig));
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

    private readonly Lazy<Task> _disposeTask = null!;
    public readonly ListenerConfig Config;
    public readonly IpcServer Server;
    public readonly ILogger Logger;

    protected Listener(IpcServer server, ListenerConfig config)
    {
        Config = config;
        Server = server;
        Logger = server.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(config.DebugName);
        _disposeTask = new(DisposeCore);
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(_disposeTask.Value);

    protected abstract Task DisposeCore();
}

internal sealed class Listener<TConfig, TListenerState, TConnectionState> : Listener, IAsyncDisposable
    where TConfig : ListenerConfig, IListenerConfig<TConfig, TListenerState, TConnectionState>
    where TListenerState : IAsyncDisposable
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
        Log($"Stopping listener {Config.DebugName}...");
        _cts.Cancel();
        try
        {
            await _listeningTask;
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == _cts.Token)
        {
            Log($"Stopping listener {Config.DebugName} threw OCE.");
        }
        catch (Exception ex)
        {
            LogError(ex, $"Stopping listener {Config.DebugName} failed.");
        }
        await State.DisposeAsync();
        _cts.Dispose();
    }

    private async Task Listen(CancellationToken ct)
    {
        Log($"Starting listener {Config.DebugName}...");

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
        try
        {
            var network = await serverConnection.AcceptClient(ct);
            serverConnection.Listen(network, ct).LogException(Logger, Config.DebugName);
        }
        catch (Exception ex)
        {
            serverConnection.Dispose();
            if (!ct.IsCancellationRequested)
            {
                Logger.LogException(ex, Config.DebugName);
            }
        }
    }

    public override string ToString() => Config.ToString();
}
