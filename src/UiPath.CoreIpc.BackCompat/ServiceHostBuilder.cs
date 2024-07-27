using UiPath.Ipc;
using UiPath.Ipc.Transport.NamedPipe;
using UiPath.Ipc.Transport.Tcp;
using UiPath.Ipc.Transport.WebSocket;

namespace UiPath.Ipc.BackCompat;

public class ServiceHostBuilder
{
    private readonly List<ListenerConfig> _listeners = new();
    private readonly IServiceProvider _serviceProvider;

    public Dictionary<string, EndpointSettings> Endpoints { get; } = new();

    public ServiceHostBuilder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ServiceHostBuilder AddEndpoint(EndpointSettings settings)
    {
        settings = settings.WithServiceProvider(_serviceProvider);
        Endpoints.Add(settings.Name, settings);
        return this;
    }

    public ServiceHost Build() => new(_serviceProvider, _listeners, Endpoints);

    public ServiceHostBuilder UseListener<T>(T listener) where T : ListenerConfig
    {
        _listeners.Add(listener);
        return this;
    }
}

public static class ServiceHostBuilderExtensions
{
    public static ServiceHostBuilder AddEndpoint<TContract>(this ServiceHostBuilder serviceHostBuilder, TContract? serviceInstance = null)
    where TContract : class
    => serviceHostBuilder.AddEndpoint(new EndpointSettings<TContract>(serviceInstance));

    public static ServiceHostBuilder UseNamedPipes(this ServiceHostBuilder builder, NamedPipeListener listener)
    => builder.UseListener(listener);

    public static ServiceHostBuilder UseTcp(this ServiceHostBuilder builder, TcpListener listener)
    => builder.UseListener(listener);

    public static ServiceHostBuilder UseWebSockets(this ServiceHostBuilder builder, WebSocketListener listener)
    => builder.UseListener(listener);
}