using System.Net;
using UiPath.CoreIpc.Tcp;
namespace UiPath.CoreIpc.Tests;
public class SystemTcpTests : SystemTests<TcpClientBuilder<ISystemService>>
{
    int _port = 3131 + GetCount();
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseTcp(Configure(new TcpSettings(GetEndPoint())));
    protected override TcpClientBuilder<ISystemService> CreateSystemClientBuilder() => new(GetEndPoint());
    [Fact]
    public override  async void BeforeCallServerSide()
    {
        _port++;
        base.BeforeCallServerSide();
    }
    IPEndPoint GetEndPoint() => new(IPAddress.Loopback, _port);

    public override void Initialize()
    {
        _port = GetAvailablePort();
    }
}
public class ComputingTcpTests : ComputingTests<TcpClientBuilder<IComputingService, IComputingCallback>>
{
    public override void Initialize()
    {
        ComputingEndPoint = new(IPAddress.Loopback, GetAvailablePort());
    }

    protected IPEndPoint ComputingEndPoint;
    protected override TcpClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null) =>
        new TcpClientBuilder<IComputingService, IComputingCallback>(ComputingEndPoint, _serviceProvider)
            .RequestTimeout(RequestTimeout)
            .CallbackInstance(_computingCallback)
            .TaskScheduler(taskScheduler);
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseTcp(Configure(new TcpSettings(ComputingEndPoint)));
}