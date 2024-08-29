namespace UiPath.Ipc;

public sealed class IpcClient
{
    public required ClientConfig Config { get; init; }
    public required ClientTransport Transport { get; init; }

    private readonly ConcurrentDictionary<Type, ServiceClient> _clients = new();
    private ServiceClient GetServiceClient(Type proxyType)
    {
        return _clients.GetOrAdd(proxyType, Create);

        ServiceClient Create(Type proxyType) => new ServiceClientProper(this, proxyType);
    }
    public TProxy GetProxy<TProxy>() where TProxy : class
    => GetServiceClient(typeof(TProxy)).GetProxy<TProxy>();

    internal void Validate()
    {
        if (Config is null)
        {
            throw new InvalidOperationException($"{Config} is required.");
        }
        if (Transport is null)
        {
            throw new InvalidOperationException($"{Transport} is required.");
        }

        Config.Validate();
        Transport.Validate();

        Config.DebugName ??= Transport.ToString();
    }
}
