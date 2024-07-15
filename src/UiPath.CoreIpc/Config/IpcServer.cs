using System.IO.Pipes;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using UiPath.Ipc.NamedPipe;
using UiPath.Ipc.Tcp;
using UiPath.Ipc.WebSockets;

namespace UiPath.Ipc;

public readonly record struct IpcServerConfig
{
    public required IServiceProvider ServiceProvider { get; init; }
    public required EndpointCollection Endpoints { get; init; }
    public IReadOnlyList<Type>? Callbacks { get; init; }
    public required IReadOnlyList<ListenerConfig> Listeners { get; init; }
    public TaskScheduler? Scheduler { get; init; }

    internal IpcServerConfig WithServer(IpcServer server)
    => this with
    {
        Listeners = Listeners.Select(listener => listener.WithServer(server)).ToArray()
    };
}

public sealed class IpcServer : IAsyncDisposable
{
    public required IpcServerConfig Config { get; init; }

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

        var configWithServer = Config.WithServer(this);

        var disposables = new AsyncCollectionDisposable();
        try
        {
            foreach (var listenerConfig in configWithServer.Listeners)
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
        errors = Config.Listeners.SelectMany(PrefixErrors).ToArray();
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

public abstract record ListenerConfig : EndpointConfig
{
    public int ConcurrentAccepts { get; init; } = 5;
    public byte MaxReceivedMessageSizeInMegabytes { get; init; } = 2;
    public X509Certificate? Certificate { get; init; }

    private IpcServer? _server;
    internal IpcServer? Server
    {
        get => _server;
        init => _server = value;
    }

    internal ListenerConfig WithServer(IpcServer server)
    {
        var result = (ShallowClone() as ListenerConfig)!;
        result._server = server;
        return result;
    }

    internal protected virtual string DebugName => GetType().Name;
    public abstract IAsyncDisposable CreateListener(IpcServer server);

    internal protected virtual IEnumerable<string> Validate() => Enumerable.Empty<string>();

    internal override RouterConfig CreateRouterConfig()
    {
        if (Server is null)
        {
            throw new InvalidOperationException();
        }

        var endpoints = Server.Config.Endpoints.ToDictionary(pair => pair.Key.Name, CreateEndpointSettings);
        return new RouterConfig(endpoints);

        EndpointSettings CreateEndpointSettings(KeyValuePair<Type, object?> pair)
        {
            if (pair.Value is null)
            {
                if (Server.Config.ServiceProvider is null)
                {
                    throw new InvalidOperationException();
                }

                return new EndpointSettings(pair.Key, Server.Config.ServiceProvider)
                {
                    BeforeCall = null,
                    Scheduler = Server.Config.Scheduler.OrDefault(),
                };
            }

            return new EndpointSettings(pair.Key, pair.Value)
            {
                BeforeCall = null,
                Scheduler = Server.Config.Scheduler.OrDefault(),
            };
        }
    }
}

public abstract record ListenerConfig<TListener> : ListenerConfig where TListener : Listener, new()
{
    public sealed override IAsyncDisposable CreateListener(IpcServer server)
    {
        var listener = new TListener()
        {
            Server = server,
            Config = this
        };
        listener.InitializeCore();
        return listener;
    }
}

public interface IAsyncStream
{
    ValueTask<int> Read(Memory<byte> memory, CancellationToken ct);
    ValueTask Write(ReadOnlyMemory<byte> memory, CancellationToken ct);
    ValueTask Flush(CancellationToken ct);
}

public abstract record ListenerConfig<TListenerState, TConnectionState> : ListenerConfig
    where TListenerState : IAsyncDisposable
{
    protected internal abstract TListenerState CreateListenerState(IpcServer server);
    protected internal abstract ValueTask<IAsyncStream> AwaitConnection(TListenerState listenerState, CancellationToken ct);

    public sealed override IAsyncDisposable CreateListener(IpcServer server)
    => new ListenerAdapter(CreateListenerState(server), this, server);

    private sealed class ListenerAdapter : Listener
    {
        private readonly TListenerState _listenerState;
        private readonly ListenerConfig<TListenerState, TConnectionState> _listenerConfig;
        private readonly IpcServer _server;

        public ListenerAdapter(TListenerState listenerState, ListenerConfig<TListenerState, TConnectionState> listenerConfig, IpcServer server)
        {
            _listenerState = listenerState;
            _listenerConfig = listenerConfig;
            _server = server;
            Config = listenerConfig;
            Server = server;
            InitializeCore();
        }

        protected override ServerConnection CreateServerConnection()
        {
            return new ConnectionAdapter(_listenerState, _listenerConfig, _server, this);
        }
    }

    private sealed class ConnectionAdapter : ServerConnection
    {
        private readonly TListenerState _listenerState;
        private readonly ListenerConfig<TListenerState, TConnectionState> _listenerConfig;
        private readonly IpcServer _server;
        private readonly ListenerAdapter _listener;

        public ConnectionAdapter(TListenerState listenerState, ListenerConfig<TListenerState, TConnectionState> listenerConfig, IpcServer server, ListenerAdapter listenerAdapter)
        {
            _listenerState = listenerState;
            _listenerConfig = listenerConfig;
            _server = server;
            Listener = _listener = listenerAdapter;            
        }

        public override async Task<Stream> AcceptClient(CancellationToken ct)
        {
            var asyncStream = await _listenerConfig.AwaitConnection(_listenerState, ct);
            return new AsyncStreamAdapter(asyncStream);
        }
    }

    private sealed class AsyncStreamAdapter : Stream
    {
        private readonly IAsyncStream _target;

        public AsyncStreamAdapter(IAsyncStream stream)
        {
            _target = stream;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _target.Read(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _target.Write(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();
        public override Task FlushAsync(CancellationToken cancellationToken)
        => _target.Flush(cancellationToken).AsTask();

        public override void Flush() => throw new NotImplementedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
        public override void SetLength(long value) => throw new NotImplementedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }
}

public sealed record NamedPipeListenerConfig : ListenerConfig<NamedPipeListener>
{
    public required string PipeName { get; init; }
    public string ServerName { get; init; } = ".";
    public Action<PipeSecurity>? AccessControl { get; init; }

    internal protected override string DebugName => ServerName is "." ? PipeName : $@"{ServerName}\{PipeName}";

    internal protected override IEnumerable<string> Validate()
    {
        if (PipeName is null or "") { yield return $"{nameof(PipeName)} is not a valid pipe name."; }
        if (ServerName is null or "") { yield return $"{nameof(ServerName)} is a valid server name."; }
    }
}
public sealed record TcpListenerConfig : ListenerConfig<TcpListener>
{
    public required IPEndPoint EndPoint { get; init; }

    internal protected override string DebugName => EndPoint.ToString();

    protected internal override IEnumerable<string> Validate()
    {
        if (EndPoint is null) { yield return $"{nameof(EndPoint)} was not set."; }
    }
}
public sealed record WebSocketListenerConfig : ListenerConfig<WebSocketListener>
{
    public required Accept Accept { get; init; }
    internal protected override string DebugName => "";

    protected internal override IEnumerable<string> Validate()
    {
        if (Accept is null) { yield return $"{nameof(Accept)} was not set."; }
    }
}

public abstract record ListenerId
{
}
