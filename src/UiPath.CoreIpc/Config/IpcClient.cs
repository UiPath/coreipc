using System.ComponentModel;

namespace UiPath.Ipc;

public sealed class IpcClient : IpcBase, IClientConfig
{
    public EndpointCollection? Callbacks { get; set; }

    public ILogger? Logger { get; init; }
    public BeforeConnectHandler? BeforeConnect { get; set; }
    public BeforeCallHandler? BeforeOutgoingCall { get; set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public string DebugName { get; set; } = null!;

    public required ClientTransport Transport { get; init; }

    string IClientConfig.GetComputedDebugName() => DebugName ?? Transport.ToString();

    private readonly ConcurrentDictionary<Type, ServiceClient> _clients = new();
    private ServiceClient GetServiceClient(Type proxyType)
    {
        return _clients.GetOrAdd(proxyType, Create);

        ServiceClient Create(Type proxyType) => new ServiceClientProper(this, proxyType);        
    }
    public TProxy GetProxy<TProxy>() where TProxy : class => GetServiceClient(typeof(TProxy)).GetProxy<TProxy>();

    internal void Validate()
    {
        var haveDeferredInjectedCallbacks = Callbacks?.Any(x => x.Service.MaybeGetServiceProvider() is null && x.Service.MaybeGetInstance() is null) ?? false;

        if (haveDeferredInjectedCallbacks && ServiceProvider is null)
        {
            throw new InvalidOperationException("ServiceProvider is required when you register injectable callbacks. Consider registering a callback instance.");
        }

        if (Transport is null)
        {
            throw new InvalidOperationException($"{Transport} is required.");
        }

        Transport.Validate();
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

    internal RouterConfig CreateCallbackRouterConfig()
    => RouterConfig.From(
        Callbacks.OrDefault(),
        endpoint => endpoint with
        {
            BeforeIncomingCall = null, // callbacks don't support BeforeCall
            Scheduler = endpoint.Scheduler ?? Scheduler
        });
}
