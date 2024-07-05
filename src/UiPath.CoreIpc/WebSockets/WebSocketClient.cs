using System.Net.WebSockets;

namespace UiPath.Ipc.WebSockets;

class WebSocketClient<TInterface> : ServiceClient<TInterface> where TInterface : class
{
    private readonly WebSocketConnectionKey _key;

    public WebSocketClient(WebSocketConnectionKey key, ConnectionConfig config) : base(config, key)
    {
        _key = key;
    }

    public override string DebugName => base.DebugName ?? _key.Uri.ToString();
}

internal class WebSocketClientConnection : ClientConnection
{
    private readonly WebSocketConnectionKey _key;
    private ClientWebSocket? _clientWebSocket;

    public WebSocketClientConnection(WebSocketConnectionKey key) : base(key)
    {
        _key = key;
    }

    public override bool Connected => _clientWebSocket is { State: WebSocketState.Open };

    protected override void Dispose(bool disposing)
    {
        _clientWebSocket?.Dispose();
        base.Dispose(disposing);
    }

    public override async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        _clientWebSocket = new();
        var uri = _key.Uri;
        await _clientWebSocket.ConnectAsync(uri, cancellationToken);
        return new WebSocketStream(_clientWebSocket);
    }
}
