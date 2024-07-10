namespace UiPath.Ipc.WebSockets;

public sealed class WebSocketListener : Listener<WebSocketListenerConfig, WebSocketListener.WebSocketConnection>
{
    public sealed class WebSocketConnection : ServerConnection<WebSocketListener>
    {
        public override async Task<Stream> AcceptClient(CancellationToken cancellationToken)
        {
            var webSocket = await Listener.Config.Accept(cancellationToken);
            return new WebSocketStream(webSocket);
        }
    }
}
