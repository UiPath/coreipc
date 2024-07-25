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
    {
        throw new NotImplementedException();
    }

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
    where TSelf : IClient<TState, TSelf>
    where TState : IClientState<TSelf, TState>
{
}

public interface IClientState<TClient, TSelf>
    where TSelf : IClientState<TClient, TSelf>
    where TClient : IClient<TSelf, TClient>
{
    Network? Network { get; }

    bool IsConnected();
    ValueTask Connect(TClient client, CancellationToken ct);
}