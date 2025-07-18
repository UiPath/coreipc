using System.Net;
using UiPath.Ipc.Transport.Tcp;
using Xunit.Abstractions;

namespace UiPath.CoreIpc.Tests;

public sealed class ComputingTestsOverTcp : ComputingTests
{
    private readonly IPEndPoint _endPoint = NetworkHelper.FindFreeLocalPort();

    public ComputingTestsOverTcp(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected override async Task<ListenerConfig> CreateListener()
    => new TcpListener
    {
        EndPoint = _endPoint,
    };

    protected override ClientTransport CreateClientTransport()
    => new TcpTransport() { EndPoint = _endPoint };

    public override IAsyncDisposable? RandomTransportPair(out ListenerConfig listener, out ClientTransport transport)
    {
        var endPoint = NetworkHelper.FindFreeLocalPort();
        listener = new TcpListener() { EndPoint = endPoint };
        transport = new TcpTransport() { EndPoint = endPoint };
        return null;
    }

    public override ExternalServerParams RandomServerParams()
    {
        var endPoint = NetworkHelper.FindFreeLocalPort();
        return new(ServerKind.Tcp, Port: endPoint.Port);
    }
}
