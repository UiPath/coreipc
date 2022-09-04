using System.Net;
using System.Threading.Tasks;
using UiPath.CoreIpc.Tcp;
using Xunit;

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
}
public class ComputingTcpTests : ComputingTests<TcpClientBuilder<IComputingService, IComputingCallback>>
{
    protected static readonly IPEndPoint ComputingEndPoint = new(IPAddress.Loopback, 2121+GetCount());
    protected override TcpClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null) =>
        new TcpClientBuilder<IComputingService, IComputingCallback>(ComputingEndPoint, _serviceProvider)
            .RequestTimeout(RequestTimeout)
            .CallbackInstance(_computingCallback)
            .TaskScheduler(taskScheduler);
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseTcp(Configure(new TcpSettings(ComputingEndPoint)));
}