using System.Diagnostics.CodeAnalysis;
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
        Listeners = Listeners.Select(listener => listener with
        {
            Server = server
        }).ToArray()
    };
}

public sealed class IpcServer : IAsyncDisposable
{
    public required IpcServerConfig Config { get; init; }

    private readonly Lazy<Task<IAsyncDisposable?>> _started;

    public IpcServer() => _started = new(Start);

    public void EnsureStarted()
    {
        if (!IsValid(out var errors))
        {
            throw new InvalidOperationException($"ValidationErrors:\r\n{string.Join("\r\n", errors)}");
        }

        _ = _started.Value;
    }

    private async Task<IAsyncDisposable?> Start()
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

    internal IpcServer? Server { get; init; }

    internal abstract string DebugName { get; }
    public abstract IAsyncDisposable CreateListener(IpcServer server);

    internal protected abstract IEnumerable<string> Validate();

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
public sealed record NamedPipeListenerConfig : ListenerConfig
{
    public required string PipeName { get; init; }
    public string ServerName { get; init; } = ".";
    public Action<PipeSecurity>? AccessControl { get; init; }

    internal override string DebugName => ServerName is "." ? PipeName : $@"{ServerName}\{PipeName}";
    public override IAsyncDisposable CreateListener(IpcServer server) => new NamedPipeListener(server, config: this);

    internal protected override IEnumerable<string> Validate()
    {
        if (PipeName is null or "") { yield return $"{nameof(PipeName)} is not a valid pipe name."; }
        if (ServerName is null or "") { yield return $"{nameof(ServerName)} is a valid server name."; }
    }
}
public sealed record TcpListenerConfig : ListenerConfig
{
    public required IPEndPoint EndPoint { get; init; }

    internal override string DebugName => EndPoint.ToString();

    public override IAsyncDisposable CreateListener(IpcServer server) => new TcpListener(server, config: this);

    protected internal override IEnumerable<string> Validate()
    {
        if (EndPoint is null) { yield return $"{nameof(EndPoint)} was not set."; }
    }
}
public sealed record WebSocketListenerConfig : ListenerConfig
{
    public required Accept Accept { get; init; }
    internal override string DebugName => "";
    public override IAsyncDisposable CreateListener(IpcServer server) => new WebSocketListener(server, config: this);

    protected internal override IEnumerable<string> Validate()
    {
        if (Accept is null) { yield return $"{nameof(Accept)} was not set."; }
    }
}

public abstract record ListenerId
{
}
public abstract record ListenerId<TTransport> : ListenerId
{
}
public sealed record NamedPipeListenerId : ListenerId<NamedPipeListenerId.Transport>
{
    public string PipeName { get; }

    public NamedPipeListenerId(string pipeName) => PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));

    public sealed record Transport
    {
        public Action<PipeSecurity>? PipeSecurity { get; init; }
    }
}
public sealed record TCPListenerId : ListenerId
{
    public IPEndPoint EndPoint { get; }

    public TCPListenerId(IPEndPoint endPoint) => EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
}
public sealed record WebSocketListenerId : ListenerId
{
    public int Port { get; }
    public required Accept Accept { get; init; }

    public WebSocketListenerId(int port) => Port = port;

    public bool Equals(WebSocketListenerId? other) => other is not null && Port == other.Port;
    public override int GetHashCode() => Port;
}
