namespace UiPath.Ipc;

public sealed class IpcServer : IAsyncDisposable
{
    static IpcServer()
    {
        Telemetry.ProcessStart.EnsureInitialized();
    }

    public required IServiceProvider ServiceProvider { get; init; }
    public required EndpointCollection Endpoints { get; init; }
    public required IReadOnlyList<ListenerConfig> Listeners { get; init; }
    public TaskScheduler? Scheduler { get; init; }

    private readonly Lazy<Task<IAsyncDisposable?>> _started;
    private readonly TaskCompletionSource<object?> _tcsStopped = new();

    public IpcServer() => _started = new(StartCore);

    public void Start()
    {
        if (!IsValid(out var errors))
        {
            throw new InvalidOperationException($"ValidationErrors:\r\n{string.Join("\r\n", errors)}");
        }

        _ = _started.Value;
    }
    public Task WaitForStart() => _started.Value;
    public Task WaitForStop() => _tcsStopped.Task;

    private async Task<IAsyncDisposable?> StartCore()
    {
        if (!IsValid(out _))
        {
            return null;
        }

        var disposables = new StopAdapter(this);
        try
        {
            foreach (var listenerConfig in Listeners)
            {
                disposables.Add(Listener.Create(this, listenerConfig));
            }
            return disposables;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Failed to start server. Ex: {ex}");
            disposables.SetException(ex);
            await disposables.DisposeAsync();
            throw;
        }
    }

    private bool IsValid(out IReadOnlyList<string> errors)
    {
        errors = Listeners.SelectMany(PrefixErrors).ToArray();
        return errors is { Count: 0 };

        static IEnumerable<string> PrefixErrors(ListenerConfig config)
        => config.Validate().Select(error => $"{config.GetType().Name}: {error}");
    }

    public async ValueTask DisposeAsync()
    {
        var maybeLogger = ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(IpcServer));

        await new Telemetry.IpcServerDispose { Logger = maybeLogger }.Monitor(
            async () =>
            {
                await ((await _started.Value)?.DisposeAsync() ?? default);
            });
    }

    private sealed class StopAdapter : IAsyncDisposable
    {
        private readonly List<IAsyncDisposable> _items = new();
        private readonly IpcServer _server;
        private Exception? _exception;

        public StopAdapter(IpcServer server) => _server = server;

        public void Add(IAsyncDisposable item) => _items.Add(item);

        public void SetException(Exception ex) => _exception = ex;

        public async ValueTask DisposeAsync()
        {
            foreach (var item in _items)
            {
                await item.DisposeAsync();
            }

            if (_exception is not null)
            {
                _server._tcsStopped.TrySetException(_exception);
                return;
            }

            _server._tcsStopped.TrySetResult(null);
        }
    }
}
