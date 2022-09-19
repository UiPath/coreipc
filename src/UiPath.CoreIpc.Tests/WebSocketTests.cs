using UiPath.CoreIpc.WebSockets;
namespace UiPath.CoreIpc.Tests;
public class SystemWebSocketTests : SystemTests<WebSocketClientBuilder<ISystemService>>
{
    int _port = 1313 + GetCount();
    HttpSysWebSocketsListener _listener;
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder)
    {
        _listener = new HttpSysWebSocketsListener("http" + GetEndPoint());
        return serviceHostBuilder.UseWebSockets(Configure(new WebSocketSettings(_listener.Accept)));
    }
    public override void Dispose()
    {
        _listener?.Dispose();
        base.Dispose();
    }
    protected override WebSocketClientBuilder<ISystemService> CreateSystemClientBuilder() => new(new("ws"+GetEndPoint()));
    [Fact]
    public override  async void BeforeCallServerSide()
    {
        _port++;
        base.BeforeCallServerSide();
    }
    string GetEndPoint() => $"://localhost:{_port}/";
}
public class ComputingWebSocketsTests : ComputingTests<WebSocketClientBuilder<IComputingService, IComputingCallback>>
{
    protected static readonly string ComputingEndPoint = $"://localhost:{1212+GetCount()}/";
    HttpSysWebSocketsListener _listener;
    protected override WebSocketClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null) =>
        new WebSocketClientBuilder<IComputingService, IComputingCallback>(new("ws"+ComputingEndPoint), _serviceProvider)
            .RequestTimeout(RequestTimeout)
            .CallbackInstance(_computingCallback)
            .TaskScheduler(taskScheduler);
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder)
    {
        _listener = new HttpSysWebSocketsListener("http" + ComputingEndPoint);
        return serviceHostBuilder.UseWebSockets(Configure(new WebSocketSettings(_listener.Accept)));
    }
    public override void Dispose()
    {
        _listener?.Dispose();
        base.Dispose();
    }
}