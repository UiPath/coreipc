using System.Security.Cryptography.X509Certificates;

namespace UiPath.Ipc;

public abstract record ListenerConfig : EndpointConfig
{
    public int ConcurrentAccepts { get; init; } = 5;
    public byte MaxReceivedMessageSizeInMegabytes { get; init; } = 2;
    public X509Certificate? Certificate { get; init; }

    internal int MaxMessageSize => MaxReceivedMessageSizeInMegabytes * 1024 * 1024;

    internal string DebugName => GetType().Name;
    internal IEnumerable<string> Validate() => Enumerable.Empty<string>();

    internal override RouterConfig CreateRouterConfig(IpcServer server)
    => new RouterConfig(server.Endpoints.ToDictionary(
        static x => x.Service.Type.Name,
        x => x with { Scheduler = x.Scheduler ?? server.Scheduler }));
}
