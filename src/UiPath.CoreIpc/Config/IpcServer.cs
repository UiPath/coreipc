namespace UiPath.Ipc;

public sealed class IpcServer : Peer, IAsyncDisposable
{
    public required EndpointCollection Endpoints { get; init; }
    public required ServerTransport Transport { get; init; }

    private readonly Lazy<Listener> _listener;
    private readonly TaskCompletionSource<object?> _tcsStopped = new();

    public IpcServer()
    {
        _listener = new(() => Listener.Create(server: this));
    }

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

    private Listener? StartCore()
    {
        if (!IsValid(out _))
        {
            return null;
        }

        try
        {
            return Listener.Create(this, Transport);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Failed to start server. Ex: {ex}");
            _tcsStopped.TrySetException(ex);
            disposables.SetException(ex);
            await disposables.DisposeAsync();
            throw;
        }
    }

    private bool IsValid(out IReadOnlyList<string> errors)
    {
        errors = PrefixErrors(Transport).ToArray();
        return errors is { Count: 0 };

        static IEnumerable<string> PrefixErrors(ServerTransport transport)
        => transport.Validate().Select(error => $"{transport.GetType().Name}: {error}");
    }

    public async ValueTask DisposeAsync()
    {
        var maybeLogger = ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(IpcServer));

        await ((await _started.Value)?.DisposeAsync() ?? default);
    }
}
