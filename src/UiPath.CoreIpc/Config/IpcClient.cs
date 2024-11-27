namespace UiPath.Ipc;

public sealed class IpcClient : IpcBase, IClientConfig
{
    public ContractCollection? Callbacks { get; set; }

    public ILogger? Logger { get; init; }
    public BeforeConnectHandler? BeforeConnect { get; set; }
    public BeforeCallHandler? BeforeOutgoingCall { get; set; }

    internal string DebugName { get; set; } = null!;

    public required ClientTransport Transport { get; init; }

    string IClientConfig.GetComputedDebugName() => DebugName ?? Transport.ToString();

    private readonly ConcurrentDictionary<Type, ServiceClient> _clients = new();
    private ServiceClient GetServiceClient(Type proxyType)
    {
        return _clients.GetOrAdd(proxyType, Create);

        ServiceClient Create(Type proxyType) => new ServiceClientProper(this, proxyType);        
    }
    public TProxy GetProxy<TProxy>() where TProxy : class => GetServiceClient(typeof(TProxy)).GetProxy<TProxy>();

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
        endpoint =>
        {
            var clone = new ContractSettings(endpoint);
            clone.BeforeIncomingCall = null; // callbacks don't support BeforeIncomingCall
            clone.Scheduler ??= Scheduler; 
            return clone;
        });
}
