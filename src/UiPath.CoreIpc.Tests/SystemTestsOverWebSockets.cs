using UiPath.Ipc.Transport.WebSocket;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public sealed class SystemTestsOverWebSockets : SystemTests
{
    private readonly WebSocketContext _webSocketContext = new();

    public SystemTestsOverWebSockets(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected override async Task DisposeAsync()
    {
        await _webSocketContext.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override async Task<ServerTransport> CreateServerTransport()
    {
        var listener = new WebSocketServerTransport
        {
            Accept = _webSocketContext.Accept,
            ConcurrentAccepts = 1,
        };
        await Task.Delay(500); // Wait for the listener to start.
        return listener;
    }

    protected override ClientTransport CreateClientTransport()
    => new WebSocketClientTransport() { Uri = _webSocketContext.ClientUri };
}
