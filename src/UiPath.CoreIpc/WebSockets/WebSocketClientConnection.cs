using System.Net.WebSockets;

namespace UiPath.Ipc.WebSockets;

public class WebSocketClientConnection : ClientConnection
{
    private WebSocketConnectionKey Key => ConnectionKey as WebSocketConnectionKey ?? throw new InvalidOperationException();
    private ClientWebSocket? _clientWebSocket;

    public override bool Connected => _clientWebSocket is { State: WebSocketState.Open };

    protected override void Dispose(bool disposing)
    {
        _clientWebSocket?.Dispose();
        base.Dispose(disposing);
    }

    public override async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        _clientWebSocket = new();
        var uri = Key.Uri;
        await _clientWebSocket.ConnectAsync(uri, cancellationToken);
        return new WebSocketStream(_clientWebSocket);
    }
}
