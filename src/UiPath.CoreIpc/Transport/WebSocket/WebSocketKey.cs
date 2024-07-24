using System.Net.WebSockets;

namespace UiPath.Ipc.Transport.WebSocket;

using IWebSocketKey = IConnectionKey<WebSocketKey, WebSocketConnectionState>;

public sealed record WebSocketKey : IWebSocketKey
{
    public required Uri Uri { get; init; }

    public required ClientBase DefaultConfig { get; init; }

    async Task<OneOf<IAsyncStream, Stream>> IWebSocketKey.Connect(CancellationToken ct)
    {
        var clientWebSocket = new ClientWebSocket();
        await clientWebSocket.ConnectAsync(Uri, ct);
        return new WebSocketStream(clientWebSocket);
    }
}

internal sealed class WebSocketConnectionState
{
}
