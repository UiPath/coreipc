namespace UiPath.Ipc;

public sealed class IpcClient
{
    static IpcClient()
    {
        Telemetry.ProcessStart.EnsureInitialized();
    }

    private Telemetry.IpcClientInitialized _telemetry = null!;

    private ClientTransport _transport = null!;

    public required ClientConfig Config { get; init; }
    public required ClientTransport Transport
    {
        get => _transport;
        init
        {
            _transport = value;
            _telemetry = new Telemetry.IpcClientInitialized { Transport = value?.ToString()! }.Log();
        }
    }

    private readonly ConcurrentDictionary<Type, ServiceClient> _clients = new();
    private ServiceClient GetServiceClient(Type proxyType)
    {
        return _clients.GetOrAdd(proxyType, Create);

        ServiceClient Create(Type proxyType)
        {
            return new ServiceClientProper(this, proxyType, _telemetry);
        }
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
