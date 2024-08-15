using UiPath.Ipc.Transport.WebSocket;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public sealed class ComputingTestsOverWebSockets : ComputingTests
{
    private readonly WebSocketContext _webSocketContext = new();

    public ComputingTestsOverWebSockets(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected override async Task DisposeAsync()
    {
        await _webSocketContext.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override ListenerConfig CreateListener() => new WebSocketListener()
    {
        Accept = _webSocketContext.Accept,
    };
    protected override ClientTransport CreateClientTransport()
    => new WebSocketTransport() { Uri = _webSocketContext.ClientUri };
}