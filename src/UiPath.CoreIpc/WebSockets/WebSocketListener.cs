namespace UiPath.Ipc.WebSockets;

internal sealed class WebSocketListener : Listener
{
    public new WebSocketListenerConfig Config { get; }

    public WebSocketListener(IpcServer server, WebSocketListenerConfig config) : base(server, config)
    {
        Config = config;

        EnsureListening();
    }

    protected override ServerConnection CreateServerConnection() => new WebSocketConnection(this);

    private sealed class WebSocketConnection : ServerConnection
    {
        private new readonly WebSocketListener _listener;

        public WebSocketConnection(WebSocketListener listener) : base(listener)
        {
            _listener = listener;
        }

        public override async Task<Stream> AcceptClient(CancellationToken cancellationToken)
        {
            var webSocket = await _listener.Config.Accept(cancellationToken);
            return new WebSocketStream(webSocket);
        }
    }
}
