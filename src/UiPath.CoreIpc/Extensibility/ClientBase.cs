namespace UiPath.Ipc;

public abstract record ClientBase : EndpointConfig
{
    private readonly ConcurrentDictionary<Type, ServiceClient> _clients = new();
    private ServiceClient GetServiceClient(Type proxyType) => _clients.GetOrAdd(proxyType, ServiceClientProper.Create(this, proxyType));

    public IServiceProvider? ServiceProvider { get; init; }
    public EndpointCollection? Callbacks { get; init; }
    public ILogger? Logger { get; init; }
    public ConnectionFactory? ConnectionFactory { get; init; }
    public BeforeCallHandler? BeforeCall { get; init; }
    public TaskScheduler? Scheduler { get; init; }
    public ISerializer? Serializer { get; set; }

    public virtual void Validate() { }

    public TProxy GetProxy<TProxy>() where TProxy : class
    => GetServiceClient(typeof(TProxy)).GetProxy<TProxy>();

    internal void ValidateInternal()
    {
        var haveDeferredInjectedCallbacks = Callbacks?.Any(x => x.Service.MaybeGetServiceProvider() is null && x.Service.MaybeGetInstance() is null) ?? false;

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
    => RouterConfig.From(
        Callbacks.OrDefault(),
        endpoint => endpoint with
        {
            BeforeCall = endpoint.BeforeCall ?? BeforeCall,
            Scheduler = endpoint.Scheduler ?? Scheduler
        });
}

public interface IClient<TState, TSelf>
    where TSelf : ClientBase, IClient<TState, TSelf>
    where TState : class, IClientState<TSelf, TState>, new() { }

public interface IClientState<TClient, TSelf> : IDisposable
    where TSelf : class, IClientState<TClient, TSelf>, new()
    where TClient : ClientBase, IClient<TSelf, TClient>
{
    Network? Network { get; }

    bool IsConnected();
    ValueTask Connect(TClient client, CancellationToken ct);
}