using System.Net;
using UiPath.Ipc.Transport.Tcp;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public sealed class ComputingTestsOverTcp : ComputingTests
{
    private readonly IPEndPoint _endPoint = NetworkHelper.FindFreeLocalPort();

    public ComputingTestsOverTcp(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected override ListenerConfig CreateListener()
    => new TcpListener()
    {
        EndPoint = _endPoint,
    };

    protected override ClientTransport CreateClientTransport()
    => new TcpTransport() { EndPoint = _endPoint };
}
