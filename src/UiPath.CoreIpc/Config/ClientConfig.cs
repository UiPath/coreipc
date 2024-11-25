using System.ComponentModel;

namespace UiPath.Ipc;

public sealed class ClientConfig : Peer, IServiceClientConfig
{
    public EndpointCollection? Callbacks { get; init; }

    public ILogger? Logger { get; init; }
    public BeforeConnectHandler? BeforeConnect { get; init; }
    public BeforeCallHandler? BeforeCall { get; init; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public string DebugName { get; set; } = null!;

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
    Stream? Network { get; }

    bool IsConnected();
    ValueTask Connect(IpcClient client, CancellationToken ct);
}