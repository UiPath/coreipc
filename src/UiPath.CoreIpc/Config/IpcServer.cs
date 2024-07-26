namespace UiPath.Ipc;

public sealed class IpcServer : IAsyncDisposable
{
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
            await disposables.DisposeAsync();
            _tcsStopped.TrySetException(ex);
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
    => await ((await _started.Value)?.DisposeAsync() ?? default);

    private sealed class StopAdapter : IAsyncDisposable
    {
        private readonly List<IAsyncDisposable> _items = new();
        private readonly IpcServer _server;

        public StopAdapter(IpcServer server) => _server = server;

        public void Add(IAsyncDisposable item) => _items.Add(item);

        public async ValueTask DisposeAsync()
        {
            foreach (var item in _items)
            {
                await item.DisposeAsync();
            }
            _server._tcsStopped.TrySetResult(null);
        }
    }
}

public interface IAsyncStream : IAsyncDisposable
{
    ValueTask<int> Read(Memory<byte> memory, CancellationToken ct);
    ValueTask Write(ReadOnlyMemory<byte> memory, CancellationToken ct);
    ValueTask Flush(CancellationToken ct);
}
