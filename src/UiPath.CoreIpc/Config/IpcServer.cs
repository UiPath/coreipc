using System.Diagnostics.CodeAnalysis;

namespace UiPath.Ipc;

public sealed class IpcServer : IpcBase, IAsyncDisposable
{
    public required ContractCollection Endpoints { get; init; }
    public required ServerTransport Transport { get; init; }

    private readonly object _lock = new();
    private readonly TaskCompletionSource<object?> _listening = new();
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
        return _accepter!.StartedAccepting;
    }

    internal ILogger? CreateLogger(string category) => ServiceProvider.MaybeCreateLogger(category);

    private void OnNewConnection(Stream network)
    {
        ServerConnection.CreateAndListen(server: this, network, ct: _ctsActiveConnections.Token);
    }

    private void OnNewConnectionError(Exception ex)
    {
        Trace.TraceError($"Failed to accept new connection. Ex: {ex}");
    }

    internal RouterConfig CreateRouterConfig(IpcServer server) => RouterConfig.From(
        server.Endpoints,
        endpoint =>
        {
            var clone = new ContractSettings(endpoint);
            clone.Scheduler ??= server.Scheduler;
            return clone;
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
            while (!ct.IsCancellationRequested)
            {
                await TryAccept(ct); /// this method doesn't throw, and in case of non-<see cref="OperationCanceledException"/> exceptions,
                                     /// it will notify the <see cref="_newConnection"/> observer.
            }

            _newConnection.OnCompleted();
        }

        /// <summary>
        /// This method returns when a new connection is accepted, or when cancellation or another error occurs.
        /// In case of cancellation or error, it will dispose of the underlying resources and will suppress the exception.
        /// In case of an error (not a cancellation), it will notify the observer about the error.
        /// </summary>
        private async Task TryAccept(CancellationToken ct)
        {
            var slot = _serverState.CreateConnectionSlot();

            try
            {
                var taskNewConnection = slot.AwaitConnection(ct);
                _tcsStartedAccepting.TrySetResult(null);

                var newConnection = await taskNewConnection;
                _newConnection.OnNext(newConnection);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == ct)
            {
                await slot.DisposeAsync();
            }
            catch (Exception ex)
            {
                await slot.DisposeAsync();
                _newConnection.OnError(ex);
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
