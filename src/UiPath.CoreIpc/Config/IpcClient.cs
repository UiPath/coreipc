using System.Net;
using UiPath.Ipc.NamedPipe;
using UiPath.Ipc.Tcp;
using UiPath.Ipc.WebSockets;

namespace UiPath.Ipc;

public static class IpcClient
{
    private static readonly object Lock = new();
    private static readonly Dictionary<ConnectionKey, ConnectionConfig> Configs = new();

    public static void Config(ConnectionKey key, ConnectionConfig config)
    {
        config.Validate();

        lock (Lock)
        {
            Configs[key] = config;
        }
    }

    public static T Connect<T>(ConnectionKey key) where T : class
    {
        ConnectionConfig? config;
        lock (Lock)
        {
            _ = Configs.TryGetValue(key, out config);
        }
        config ??= key.DefaultConfig;

        return key.Connect<T>(config);
    }
}

public sealed record ConnectionConfig : EndpointConfig
{
    private static readonly Dictionary<string, EndpointSettings> CachedEmptyDictionary = new();
    internal static readonly ConnectionConfig Default = new();

    public IServiceProvider? ServiceProvider { get; init; }
    public EndpointCollection? Callbacks { get; init; }
    public ILogger? Logger { get; init; }
    public ConnectionFactory? ConnectionFactory { get; init; }
    public BeforeCallHandler? BeforeCall { get; init; }
    public TaskScheduler? Scheduler { get; init; }

    internal ISerializer? Serializer { get; set; }

    public void Validate()
    {
        var haveInjectedCallbacks = Callbacks?.Any(pair => pair.Value is null) ?? false;

        if (haveInjectedCallbacks && ServiceProvider is null)
        {
            throw new InvalidOperationException("ServiceProvider is required when you register injectable callbacks. Consider registering a callback instance.");
        }
    }

    internal override RouterConfig CreateCallbackRouterConfig()
    {
        var endpoints = Callbacks?.ToDictionary(
            pair => pair.Key.Name, 
            CreateEndpointSettings) ?? CachedEmptyDictionary;

        return new RouterConfig(endpoints);

        EndpointSettings CreateEndpointSettings(KeyValuePair<Type, object?> pair)
        {
            if (pair.Value is null)
            {
                if (ServiceProvider is null) { throw new InvalidOperationException(); }

                return new EndpointSettings(pair.Key, ServiceProvider)
                {
                    BeforeCall = BeforeCall,
                    Scheduler = Scheduler.OrDefault()
                };
            }

            return new EndpointSettings(pair.Key, pair.Value)
            {
                BeforeCall = BeforeCall,
                Scheduler = Scheduler.OrDefault()
            };
        }
    }
}

public abstract record ConnectionKey
{
    internal virtual ConnectionConfig DefaultConfig { get; } = ConnectionConfig.Default;

    internal abstract TProxy Connect<TProxy>(ConnectionConfig config) where TProxy : class;

    internal abstract ClientConnection CreateClientConnection();
}
public sealed record NamedPipeConnectionKey : ConnectionKey
{
    public string PipeName { get; }
    public string ServerName { get; }
    public bool AllowImpersonation { get; }

    public NamedPipeConnectionKey(string pipeName, string serverName = ".", bool allowImpersonation = false)
    {
        PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        ServerName = serverName ?? throw new ArgumentNullException(nameof(serverName));
        AllowImpersonation = allowImpersonation;
    }

    internal override TProxy Connect<TProxy>(ConnectionConfig config) => new NamedPipeClient<TProxy>(this, config).CreateProxy();

    internal override ClientConnection CreateClientConnection() => new NamedPipeClientConnection(this);
}
public sealed record TcpConnectionKey : ConnectionKey
{
    public IPEndPoint EndPoint { get; }

    public TcpConnectionKey(IPEndPoint endPoint) => EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));

    internal override TProxy Connect<TProxy>(ConnectionConfig config) => new TcpClient<TProxy>(this, config).CreateProxy();

    internal override ClientConnection CreateClientConnection() => new TcpClientConnection(this);
}
public sealed record WebSocketConnectionKey : ConnectionKey
{
    public Uri Uri { get; }

    public WebSocketConnectionKey(Uri uri) => Uri = uri ?? throw new ArgumentNullException(nameof(uri));

    internal override TProxy Connect<TProxy>(ConnectionConfig config) => new WebSocketClient<TProxy>(this, config).CreateProxy();

    internal override ClientConnection CreateClientConnection() => new WebSocketClientConnection(this);
}
