namespace UiPath.Ipc;

public sealed class IpcServer : IAsyncDisposable
{
    public required IServiceProvider ServiceProvider { get; init; }
    public required EndpointCollection Endpoints { get; init; }
    public required IReadOnlyList<ListenerConfig> Listeners { get; init; }
    public TaskScheduler? Scheduler { get; init; }

    private readonly Lazy<Task<IAsyncDisposable?>> _started;

    public IpcServer() => _started = new(StartCore);

    public async Task Start()
    {
        if (!IsValid(out var errors))
        {
            throw new InvalidOperationException($"ValidationErrors:\r\n{string.Join("\r\n", errors)}");
        }

        await _started.Value;
    }

    private async Task<IAsyncDisposable?> StartCore()
    {
        if (!IsValid(out _))
        {
            return null;
        }

        var disposables = new AsyncCollectionDisposable();
        try
        {
            foreach (var listenerConfig in Listeners)
            {
                disposables.Add(listenerConfig.CreateListener(server: this));
            }
            return disposables;
        }
        catch
        {
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
    => await ((await _started.Value)?.DisposeAsync() ?? default);

    private sealed class AsyncCollectionDisposable : IAsyncDisposable
    {
        private readonly List<IAsyncDisposable> _items = new();

        public void Add(IAsyncDisposable item) => _items.Add(item);

        public async ValueTask DisposeAsync()
        {
            foreach (var item in _items)
            {
                await item.DisposeAsync();
            }
        }
    }
}

public interface IAsyncStream
{
    ValueTask<int> Read(Memory<byte> memory, CancellationToken ct);
    ValueTask Write(ReadOnlyMemory<byte> memory, CancellationToken ct);
    ValueTask Flush(CancellationToken ct);
}

public interface IListenerConfig<TSelf, TListenerState, TConnectionState>
    where TSelf : ListenerConfig, IListenerConfig<TSelf, TListenerState, TConnectionState>
    where TListenerState : IAsyncDisposable
{
    TListenerState CreateListenerState(IpcServer server);
    TConnectionState CreateConnectionState(IpcServer server, TListenerState listenerState);
    ValueTask<Network> AwaitConnection(TListenerState listenerState, TConnectionState connectionState, CancellationToken ct);
    IEnumerable<string> Validate();
}
