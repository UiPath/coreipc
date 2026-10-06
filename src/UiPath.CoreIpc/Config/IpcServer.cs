using System.Diagnostics.CodeAnalysis;

namespace UiPath.Ipc;

public sealed class IpcServer : IpcBase, IAsyncDisposable
{
    public required ContractCollection Endpoints { get; set; }
    public required ServerTransport Transport { get; set; }

    private readonly object _lock = new();
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
        private readonly Lazy<Task> _dispose;

        public Accepter(ServerTransport transport, IObserver<Stream> connected)
        {
            _serverState = transport.CreateServerState();
            _newConnection = connected;

            // Created on Start's thread, so a named pipe exists before Start returns and a client
            // launched right after it can connect: the OS queues the connection until the slot awaits it.
            var firstSlots = CreateFirstSlots(transport.ConcurrentAccepts);
            _running = Task.WhenAll(firstSlots.Select(slot => Task.Run(() => LoopAccept(slot, _cts.Token))));
            _dispose = new(DisposeCore);
        }

        private ServerTransport.IServerConnectionSlot[] CreateFirstSlots(int count)
        {
            var slots = new List<ServerTransport.IServerConnectionSlot>(count);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    slots.Add(_serverState.CreateConnectionSlot());
                }
                return slots.ToArray();
            }
            catch
            {
                foreach (var slot in slots)
                {
                    slot.DisposeAsync().AsTask().TraceError();
                }
                _serverState.DisposeAsync().AsTask().TraceError();
                throw;
            }
        }

        public ValueTask DisposeAsync() => new(_dispose.Value);

        private async Task DisposeCore()
        { 
            _cts.Cancel();
            await _running;
            _cts.Dispose();
        }


        private async Task LoopAccept(ServerTransport.IServerConnectionSlot firstSlot, CancellationToken ct)
        {
            var slot = firstSlot;
            // Unconditional: every slot goes through TryAccept, which disposes it when the token is already cancelled.
            while (true)
            {
                await TryAccept(slot, ct); /// this method doesn't throw, and in case of non-<see cref="OperationCanceledException"/> exceptions,
                                           /// it will notify the <see cref="_newConnection"/> observer.
                if (ct.IsCancellationRequested)
                {
                    break;
                }
                slot = _serverState.CreateConnectionSlot();
            }

            _newConnection.OnCompleted();
        }

        /// <summary>
        /// This method returns when a new connection is accepted, or when cancellation or another error occurs.
        /// In case of cancellation or error, it will dispose of the underlying resources and will suppress the exception.
        /// In case of a genuine error it will notify the observer; an error that surfaces while cancellation is already
        /// requested (a shutdown-race, e.g. a broken/disposed pipe) is treated as expected teardown and NOT reported.
        /// </summary>
        private async Task TryAccept(ServerTransport.IServerConnectionSlot slot, CancellationToken ct)
        {
            try
            {
                var newConnection = await slot.AwaitConnection(ct);
                _newConnection.OnNext(newConnection);
            }
            catch (OperationCanceledException)
            {
                await slot.DisposeAsync();
                /// we don't notify the observer, as <see cref="OperationCanceledException"/> is expected
            }
            catch (Exception ex)
            {
                await slot.DisposeAsync();
                // During shutdown the slot's pipe can break or be disposed and
                // surface as a non-OCE exception (e.g. IOException "Pipe is
                // broken") instead of an OperationCanceledException. That's
                // expected teardown, not a failed accept — only report genuine
                // (non-cancelled) errors to the observer.
                if (!ct.IsCancellationRequested)
                {
                    _newConnection.OnError(ex);
                }
            }
        }

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
