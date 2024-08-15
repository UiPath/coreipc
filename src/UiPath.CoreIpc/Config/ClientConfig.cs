namespace UiPath.Ipc;

public sealed record ClientConfig : EndpointConfig
{
    public EndpointCollection? Callbacks { get; init; }

    public IServiceProvider? ServiceProvider { get; init; }
    public ILogger? Logger { get; init; }
    public BeforeCallHandler? BeforeCall { get; init; }
    public TaskScheduler? Scheduler { get; init; }
    public ISerializer? Serializer { get; set; }

    internal void Validate()
    {
        var haveDeferredInjectedCallbacks = Callbacks?.Any(x => x.Service.MaybeGetServiceProvider() is null && x.Service.MaybeGetInstance() is null) ?? false;

        if (haveDeferredInjectedCallbacks && ServiceProvider is null)
        {
            throw new InvalidOperationException("ServiceProvider is required when you register injectable callbacks. Consider registering a callback instance.");
        }
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
            BeforeCall = null, // callbacks don't support BeforeCall
            Scheduler = endpoint.Scheduler ?? Scheduler
        });
}

public interface IClientState : IDisposable
{
    Network? Network { get; }

    bool IsConnected();
    ValueTask Connect(IpcClient client, CancellationToken ct);
}