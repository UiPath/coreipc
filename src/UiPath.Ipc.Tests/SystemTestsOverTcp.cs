using System.Net;
using UiPath.Ipc.Transport.Tcp;

namespace UiPath.Ipc.Tests;

public sealed class SystemTestsOverTcp : SystemTests
{
    private readonly IPEndPoint _endPoint = NetworkHelper.FindFreeLocalPort();

    protected override ListenerConfig CreateListener() => CommonConfigListener(new TcpListener()
    {
        EndPoint = _endPoint,
        ConcurrentAccepts = 10,
        RequestTimeout = Debugger.IsAttached ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(2),
        MaxReceivedMessageSizeInMegabytes = 1,
    });

    protected override ClientBase CreateClient() => CommonConfigClient(new TcpClient()
    {
        EndPoint = _endPoint,
    });
}
