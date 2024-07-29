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

    public TProxy GetProxy<TProxy>() where TProxy : class
    => new ServiceClientProper<TClient, TClientState>(_client, typeof(TProxy))
        .CreateProxy<TProxy>();


    internal void ValidateInternal()
    {
        var haveDeferredInjectedCallbacks = Callbacks?.Any(x => !x.Service.HasServiceProvider() && x.Service.MaybeGetInstance() is null) ?? false;

        if (haveDeferredInjectedCallbacks && ServiceProvider is null)
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
    => new RouterConfig(
        (Callbacks?.ToDictionary(
            x => x.Service.Type.Name,
            x => x with
            {
                BeforeCall = x.BeforeCall ?? BeforeCall,
                Scheduler = x.Scheduler ?? Scheduler
            })).OrDefault());
}

public interface IClient<TState, TSelf>
    where TSelf : ClientBase, IClient<TState, TSelf>
    where TState : class, IClientState<TSelf, TState>, new() { }

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