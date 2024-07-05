namespace UiPath.Ipc;

internal abstract class Listener : IAsyncDisposable
{
    private const int Megabyte = 1024 * 1024;

    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<object?> _ready = new();
    private readonly Task _listeningTask;
    private readonly Lazy<Task> _disposeTask;

    public string DebugName => Config.DebugName;
    public int MaxMessageSize => Config.MaxReceivedMessageSizeInMegabytes * Megabyte;
    public ILogger Logger { get; }

    public IpcServer Server { get; }
    public ListenerConfig Config { get; }

    protected Listener(IpcServer server, ListenerConfig listenerConfig)
    {
        Server = server;
        Config = listenerConfig;

        Logger = Server.Config.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

        _listeningTask = Listen(_cts.Token);
        _disposeTask = new(DisposeCore);
    }
    public ValueTask DisposeAsync() => new ValueTask(_disposeTask.Value);

    protected void EnsureListening() => _ = _ready.TrySetResult(null);

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
        await _ready.Task.WaitAsync(ct);
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