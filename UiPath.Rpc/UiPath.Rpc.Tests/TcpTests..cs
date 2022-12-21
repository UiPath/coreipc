using System.Net;
using UiPath.Rpc.Tcp;
namespace UiPath.Rpc.Tests;
public class SystemTcpTests : SystemTests<TcpClientBuilder<ISystemService>>
{
    int _port = 3131 + GetCount();
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseTcp(Configure(new TcpSettings(GetEndPoint())));
    protected override TcpClientBuilder<ISystemService> CreateSystemClientBuilder() => new(GetEndPoint());
    IPEndPoint GetEndPoint() => new(IPAddress.Loopback, _port);
}
public class ComputingTcpTests : ComputingTests<TcpClientBuilder<IComputingService, IComputingCallback>>
{
    protected readonly IPEndPoint ComputingEndPoint = new(IPAddress.Loopback, 2121+GetCount());
    protected override TcpClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null) =>
        new TcpClientBuilder<IComputingService, IComputingCallback>(ComputingEndPoint, _serviceProvider)
            .RequestTimeout(RequestTimeout)
            .CallbackInstance(_computingCallback)
            .TaskScheduler(taskScheduler);
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseTcp(Configure(new TcpSettings(ComputingEndPoint)));
}