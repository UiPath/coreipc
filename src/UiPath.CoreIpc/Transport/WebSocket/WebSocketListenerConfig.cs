namespace UiPath.Ipc.Transport.WebSocket;

using IWebSocketListenerConfig = IListenerConfig<WebSocketListenerConfig, WebSocketListenerState, WebSocketServerConnectionState>;

public sealed record WebSocketListenerConfig : ListenerConfig, IWebSocketListenerConfig
{
    public required Accept Accept { get; init; }

    WebSocketListenerState IWebSocketListenerConfig.CreateListenerState(IpcServer server)
    => new();

    WebSocketServerConnectionState IWebSocketListenerConfig.CreateConnectionState(IpcServer server, WebSocketListenerState listenerState)
    => new();

    async ValueTask<OneOf<IAsyncStream, Stream>> IWebSocketListenerConfig.AwaitConnection(WebSocketListenerState listenerState, WebSocketServerConnectionState connectionState, CancellationToken ct)
    => new WebSocketStream(await Accept(ct));

    IEnumerable<string> IWebSocketListenerConfig.Validate()
    {
        if (Accept is null) { yield return "Accept is required"; }
    }
}

internal sealed class WebSocketListenerState : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        throw new NotImplementedException();
    }
}

internal sealed class WebSocketServerConnectionState
{
}
