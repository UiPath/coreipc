using System.Security.Cryptography.X509Certificates;

namespace UiPath.Ipc;

public abstract class ServerTransport : Peer, IServiceClientConfig
{
    public int ConcurrentAccepts { get; init; } = 5;
    public byte MaxReceivedMessageSizeInMegabytes { get; init; } = 2;
    public X509Certificate? Certificate { get; init; }
    internal int MaxMessageSize => MaxReceivedMessageSizeInMegabytes * 1024 * 1024;

    internal IEnumerable<string> Validate() => Enumerable.Empty<string>();

    internal override RouterConfig CreateRouterConfig(IpcServer server)
    => RouterConfig.From(
        server.Endpoints,
        endpoint => endpoint with
        {
            Scheduler = endpoint.Scheduler ?? server.Scheduler
        });

    #region IServiceClientConfig
    /// Do not implement <see cref="IServiceClientConfig.RequestTimeout"/> explicitly, as it must be implicitly implemented by <see cref="Peer.RequestTimeout"/>.

    BeforeConnectHandler? IServiceClientConfig.BeforeConnect => null;
    BeforeCallHandler? IServiceClientConfig.BeforeCall => null;
    ILogger? IServiceClientConfig.Logger => null;
    ISerializer? IServiceClientConfig.Serializer => null!;
    string IServiceClientConfig.DebugName => $"CallbackClient for {this}";
    #endregion
}
