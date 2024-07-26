using UiPath.Ipc.Transport.Tcp;

namespace UiPath.Ipc;

public abstract record ClientBase : EndpointConfig
{
    public IServiceProvider? ServiceProvider { get; init; }
    public EndpointCollection? Callbacks { get; init; }
    public ILogger? Logger { get; init; }
    public ConnectionFactory? ConnectionFactory { get; init; }
    public BeforeCallHandler? BeforeCall { get; init; }
    public TaskScheduler? Scheduler { get; init; }

    internal ISerializer? Serializer { get; set; }

    public virtual void Validate() { }

    internal void ValidateInternal()
    {
        var haveInjectedCallbacks = Callbacks?.Any(pair => pair.Value is null) ?? false;

        if (haveInjectedCallbacks && ServiceProvider is null)
        {
            throw new InvalidOperationException("ServiceProvider is required when you register injectable callbacks. Consider registering a callback instance.");
        }

        Validate();
    }

    internal ILogger? GetLogger(string name)
    {
        if (Logger is not null)
        {
            return Logger;
        }

        if (ServiceProvider?.GetService<ILoggerFactory>() is not { } loggerFactory)
        {
            return null;
        }

        return loggerFactory.CreateLogger(name);
    }

    internal override RouterConfig CreateCallbackRouterConfig()
    {
        var endpoints = Callbacks?.ToDictionary(pair => pair.Key.Name, CreateEndpointSettings);
        return new RouterConfig(endpoints.OrDefault());

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

public interface IClient<TState, TSelf>
    where TSelf : ClientBase, IClient<TState, TSelf>
    where TState : class, IClientState<TSelf, TState>, new()
{
}

public static class ClientExtensions
{
    public static ProxyFactory<TClient, TClientState> GetProxyFactory<TClient, TClientState>(this IClient<TClientState, TClient> client)
        where TClient : ClientBase, IClient<TClientState, TClient>
        where TClientState : class, IClientState<TClient, TClientState>, new()
    => new(client as TClient ?? throw new ArgumentOutOfRangeException(nameof(client)));

    public readonly struct ProxyFactory<TClient, TClientState>
        where TClient : ClientBase, IClient<TClientState, TClient>
        where TClientState : class, IClientState<TClient, TClientState>, new()
    {
        private readonly TClient _client;

        internal ProxyFactory(TClient client) => _client = client;

        public TProxy GetProxy<TProxy>() where TProxy : class
        => new ServiceClientProper<TClient, TClientState>(_client, typeof(TProxy))
            .CreateProxy<TProxy>();
    }
}

public interface IClientState<TClient, TSelf> : IDisposable
    where TSelf : class, IClientState<TClient, TSelf>, new()
    where TClient : ClientBase, IClient<TSelf, TClient>
{
    Network? Network { get; }

    bool IsConnected();
    ValueTask Connect(TClient client, CancellationToken ct);
}