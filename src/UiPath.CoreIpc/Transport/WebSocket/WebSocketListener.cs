namespace UiPath.Ipc.Transport.WebSocket;

using IWebSocketListenerConfig = IListenerConfig<WebSocketListener, WebSocketListenerState, WebSocketServerConnectionState>;

public sealed record WebSocketListener : ServerTransport, IWebSocketListenerConfig
{
    public required Accept Accept { get; init; }

    WebSocketListenerState IWebSocketListenerConfig.CreateListenerState(IpcServer server)
    => new();

    WebSocketServerConnectionState IWebSocketListenerConfig.CreateConnectionState(IpcServer server, WebSocketListenerState listenerState)
    => new();

    async ValueTask<Stream> IWebSocketListenerConfig.AwaitConnection(WebSocketListenerState listenerState, WebSocketServerConnectionState connectionState, CancellationToken ct)
    {
        var webSocket = await Accept(ct);

        return new WebSocketStream(webSocket);
    }

    IEnumerable<string> IWebSocketListenerConfig.Validate()
    {
        if (Accept is null) { yield return "Accept is required"; }
    }

    public override string ToString() => "WebSocketServer";
}

internal sealed class WebSocketListenerState : IAsyncDisposable
{
    public ValueTask DisposeAsync() => default;
}

internal sealed class WebSocketServerConnectionState : IDisposable
{
    public void Dispose() { }
}
