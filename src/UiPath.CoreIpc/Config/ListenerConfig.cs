using System.Security.Cryptography.X509Certificates;

namespace UiPath.Ipc;

public abstract record ListenerConfig : EndpointConfig
{
    public int ConcurrentAccepts { get; init; } = 5;
    public byte MaxReceivedMessageSizeInMegabytes { get; init; } = 2;
    public X509Certificate? Certificate { get; init; }

    internal protected virtual string DebugName => GetType().Name;
    public abstract IAsyncDisposable CreateListener(IpcServer server);

    internal protected virtual IEnumerable<string> Validate() => Enumerable.Empty<string>();

    internal sealed override RouterConfig CreateRouterConfig(IpcServer server)
    {
        var endpoints = server.Endpoints.ToDictionary(pair => pair.Key.Name, CreateEndpointSettings);
        return new RouterConfig(endpoints);

        EndpointSettings CreateEndpointSettings(KeyValuePair<Type, object?> pair)
        {
            if (pair.Value is null)
            {
                if (server.ServiceProvider is null)
                {
                    throw new InvalidOperationException();
                }

                return new EndpointSettings(pair.Key, server.ServiceProvider)
                {
                    BeforeCall = null,
                    Scheduler = server.Scheduler.OrDefault(),
                };
            }

            return new EndpointSettings(pair.Key, pair.Value)
            {
                BeforeCall = null,
                Scheduler = server.Scheduler.OrDefault(),
            };
        }
    }
}
