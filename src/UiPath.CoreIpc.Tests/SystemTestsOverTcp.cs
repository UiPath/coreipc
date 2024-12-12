using System.Net;
using UiPath.Ipc.Transport.Tcp;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public sealed class SystemTestsOverTcp : SystemTests
{
    private readonly IPEndPoint _endPoint = NetworkHelper.FindFreeLocalPort();

    public SystemTestsOverTcp(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected sealed override async Task<ServerTransport> CreateServerTransport()
    => new TcpServerTransport
    {
        EndPoint = _endPoint,
    };

    protected override ClientTransport CreateClientTransport()
    => new TcpClientTransport() { EndPoint = _endPoint };
}
