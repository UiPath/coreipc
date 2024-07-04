using System.Net;
using UiPath.Ipc.Tcp;
namespace UiPath.Ipc.Tests;
public class SystemTcpTests : SystemTests<TcpClientBuilder<ISystemService>>
{
    int _port = 3131 + GetCount();
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseTcp(Configure(new TcpSettings(GetEndPoint())));
    protected override TcpClientBuilder<ISystemService> CreateSystemClientBuilder() => new(GetEndPoint());
    [Fact]
    public override async void BeforeCallServerSide()
    {
        _port++;
        base.BeforeCallServerSide();
    }
    IPEndPoint GetEndPoint() => new(IPAddress.Loopback, _port);
}
public class ComputingTcpTests : ComputingTests<TcpClientBuilder<IComputingService>>
{
    protected static readonly IPEndPoint ComputingEndPoint = new(IPAddress.Loopback, 2121 + GetCount());

    protected override TcpClientBuilder<IComputingService> ComputingClientBuilder(TaskScheduler taskScheduler = null)
    {
        Ipc.Callback.Set<IComputingCallback>(_computingCallback, taskScheduler);

        return new TcpClientBuilder<IComputingService>(ComputingEndPoint)
            .RequestTimeout(RequestTimeout);
    }

    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseTcp(Configure(new TcpSettings(ComputingEndPoint)));
}