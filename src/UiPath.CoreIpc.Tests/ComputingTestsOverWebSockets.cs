using UiPath.Ipc.Transport.WebSocket;
using Xunit.Abstractions;

namespace UiPath.CoreIpc.Tests;

public sealed class ComputingTestsOverWebSockets : ComputingTests
{
    private readonly WebSocketContext _webSocketContext = new();

    public ComputingTestsOverWebSockets(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected override async Task DisposeAsync()
    {
        await _webSocketContext.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override async Task<ListenerConfig> CreateListener()
    {
        var listener = new WebSocketListener
        {
            Accept = _webSocketContext.Accept,
        };
        await Task.Delay(200); // Wait for the listener to start.
        return listener;
    }

    protected override ClientTransport CreateClientTransport()
    => new WebSocketTransport() { Uri = _webSocketContext.ClientUri };

    public override IAsyncDisposable? RandomTransportPair(out ListenerConfig listener, out ClientTransport transport)
    {
        var context = new WebSocketContext();
        listener = new WebSocketListener() { Accept = context.Accept };
        transport = new WebSocketTransport() { Uri = context.ClientUri };
        return context;
    }

    public override ExternalServerParams RandomServerParams()
    {
        var endPoint = NetworkHelper.FindFreeLocalPort();
        return new(ServerKind.WebSockets, Port: endPoint.Port);
    }
}