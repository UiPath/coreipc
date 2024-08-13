using System.Net.WebSockets;

namespace UiPath.Ipc.Transport.WebSocket;

public sealed record WebSocketClient : ClientBase, IClient<WebSocketClientState, WebSocketClient>
{
    public required Uri Uri { get; init; }
    public override string ToString() => $"WebSocketClient={Uri}";
}

internal sealed class WebSocketClientState : IClientState<WebSocketClient, WebSocketClientState>
{
    private ClientWebSocket? _clientWebSocket;

    public Network? Network { get; private set; }

    public bool IsConnected() => _clientWebSocket?.State is WebSocketState.Open;

    public async ValueTask Connect(WebSocketClient client, CancellationToken ct)
    {
        _clientWebSocket = new();
        await _clientWebSocket.ConnectAsync(client.Uri, ct);
        Network = new WebSocketStream(_clientWebSocket);
    }

    public void Dispose() => _clientWebSocket?.Dispose();
}
