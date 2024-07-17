namespace UiPath.Ipc;

internal abstract class Listener : IAsyncDisposable
{
    private const int Megabyte = 1024 * 1024;

    protected readonly CancellationTokenSource _cts = new();
    private Task _listeningTask = null!;
    private Lazy<Task> _disposeTask = null!;

    public string DebugName => Config.DebugName;
    public int MaxMessageSize => Config.MaxReceivedMessageSizeInMegabytes * Megabyte;
    public ILogger Logger { get; private set; } = null!;

    public IpcServer Server { get; internal set; } = null!;
    public ListenerConfig Config { get; internal set; } = null!;

    public ValueTask DisposeAsync() => new ValueTask(_disposeTask.Value);

    internal void InitializeCore()
    {
        Logger = Server.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

        Initialize();

        _listeningTask = Listen(_cts.Token);
        _disposeTask = new(DisposeCore);
    }

    protected internal virtual void Initialize() { }

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

    protected abstract ServerConnection CreateServerConnection();
    protected virtual async Task DisposeCore()
    {
        Log($"Stopping listener {DebugName}...");
        _cts.Cancel();
        try
        {
            await _listeningTask;
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == _cts.Token)
        {
            Log($"Stopping listener {DebugName} threw OCE.");
        }
        catch (Exception ex)
        {
            LogError(ex, $"Stopping listener {DebugName} failed.");
        }
        _cts.Dispose();
    }
    private async Task Listen(CancellationToken ct)
    {
        Log($"Starting listener {DebugName}...");

        await Task.WhenAll(Enumerable.Range(1, Config.ConcurrentAccepts).Select(async _ =>
        {
            while (!ct.IsCancellationRequested)
            {
                await AcceptConnection(ct);
            }
        }));
    }
    private async Task AcceptConnection(CancellationToken token)
    {
        var serverConnection = CreateServerConnection();
        try
        {
            var network = await serverConnection.AcceptClient(token);
            serverConnection.Listen(network, token).LogException(Logger, DebugName);
        }
        catch (Exception ex)
        {
            serverConnection.Dispose();
            if (!token.IsCancellationRequested)
            {
                Logger.LogException(ex, Config.DebugName);
            }
        }
    }
}

internal class Listener<TListenerConfig, TServerConnection> : Listener 
    where TListenerConfig : ListenerConfig
    where TServerConnection : ServerConnection, new()
{
    public new TListenerConfig Config => (TListenerConfig)base.Config;

    protected sealed override ServerConnection CreateServerConnection()
    {
        var result = new TServerConnection() { Listener = this };
        result.Initialize();
        return result;
    }
}