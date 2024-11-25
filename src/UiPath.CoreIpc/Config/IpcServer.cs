using System.Diagnostics.CodeAnalysis;

namespace UiPath.Ipc;

public sealed class IpcServer : Peer, IAsyncDisposable
{
    public required EndpointCollection Endpoints { get; init; }
    public required ServerTransport Transport { get; init; }

    private readonly object _lock = new();
    private readonly TaskCompletionSource<object?> _listening = new();
    private readonly TaskCompletionSource<object?> _stopped = new();
    private readonly CancellationTokenSource _ctsActiveConnections = new();

    private bool _disposeStarted;
    private Accepter? _accepter;
    private Lazy<Task> _dispose;

    public IpcServer()
    {
        _dispose = new(DisposeCore);
    }

    public ValueTask DisposeAsync() => new(_dispose.Value);

    private async Task DisposeCore()
    {
        Accepter? accepter = null;
        lock (_lock)
        {
            _disposeStarted = true;
            accepter = _accepter;
        }

        await (accepter?.DisposeAsync() ?? default);
        _ctsActiveConnections.Cancel();
        _ctsActiveConnections.Dispose();
    }

    [MemberNotNull(nameof(Transport), nameof(_accepter))]
    public void Start()
    {
        lock (_lock)
        {
            if (_disposeStarted)
            {
                throw new ObjectDisposedException(nameof(IpcServer));
            }

            if (!IsValid(out var errors))
            {
                throw new InvalidOperationException($"ValidationErrors:\r\n{string.Join("\r\n", errors)}");
            }

            if (_accepter is not null)
            {
                return;
            }

            _accepter = new(Transport, new ObserverAdapter<Stream>()
            {
                OnNext = OnNewConnection,
                OnError = OnNewConnectionError,
            });
        }
    }

    public Task WaitForStart()
    {
        Start();
        return _accepter.StartedAccepting;
    }
    public Task WaitForStop() => _stopped.Task;

    internal ILogger? CreateLogger(string category) => ServiceProvider.MaybeCreateLogger(category);

    private void OnNewConnection(Stream network)
    {
        ServerConnection.CreateAndListen(server: this, network, ct: _ctsActiveConnections.Token);
    }

    private void OnNewConnectionError(Exception ex)
    {
        Trace.TraceError($"Failed to accept new connection. Ex: {ex}");
        _stopped.TrySetException(ex);
    }

    internal RouterConfig CreateRouterConfig(IpcServer server) => RouterConfig.From(
        server.Endpoints,
        endpoint => endpoint with
        {
            Scheduler = endpoint.Scheduler ?? server.Scheduler
        });

    private sealed class ObserverAdapter<T> : IObserver<T>
    {
        public required Action<T> OnNext { get; init; }
        public Action<Exception>? OnError { get; init; }
        public Action? OnCompleted { get; init; }

        void IObserver<T>.OnNext(T value) => OnNext(value);
        void IObserver<T>.OnError(Exception error) => OnError?.Invoke(error);
        void IObserver<T>.OnCompleted() => OnCompleted?.Invoke();
    }

    private sealed class Accepter : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly ServerTransport.IServerState _serverState;
        private readonly Task _running;
        private readonly IObserver<Stream> _newConnection;
        private readonly TaskCompletionSource<object?> _tcsStartedAccepting = new();
        private readonly Lazy<Task> _dispose;

        public Task StartedAccepting => _tcsStartedAccepting.Task;

        public Accepter(ServerTransport transport, IObserver<Stream> connected)
        {
            _serverState = transport.CreateServerState();
            _newConnection = connected;
            _running = RunOnThreadPool(LoopAccept, parallelCount: transport.ConcurrentAccepts, _cts.Token);
            _dispose = new(DisposeCore);
        }

        public ValueTask DisposeAsync() => new(_dispose.Value);

        private async Task DisposeCore()
        { 
            _cts.Cancel();
            await _running;
            _cts.Dispose();
        }


        private async Task LoopAccept(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Accept(ct);
                }
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == ct)
            {
                // Ignore
            }

            _newConnection.OnCompleted();
        }

        private async Task Accept(CancellationToken ct)
        {
            var slot = _serverState.CreateConnectionSlot();

            try
            {
                var taskNewConnection = slot.AwaitConnection(ct);
                _tcsStartedAccepting.TrySetResult(null);

                var newConnection = await taskNewConnection;
                _newConnection.OnNext(newConnection);
            }
            catch (Exception ex)
            {
                slot.Dispose();
                _newConnection.OnError(ex);
                return;
            }
        }

        private static Task RunOnThreadPool(Func<CancellationToken, Task> action, int parallelCount, CancellationToken ct)
        => Task.WhenAll(Enumerable.Range(start: 0, parallelCount).Select(_ => Task.Run(() => action(ct))));
    }

    [MemberNotNullWhen(returnValue: true, member: nameof(Transport))]
    private bool IsValid([NotNullWhen(returnValue: false)] out string? errorMessage)
    {
        if (Transport is null)
        {
            errorMessage = $"{nameof(Transport)} is not set.";
            return false;
        }

        if (string.Join("\r\n", Transport.Validate()) is { Length: > 0 } concatenation)
        {
            errorMessage = concatenation;
            return false;
        }

        errorMessage = null;
        return true;
    }
}
