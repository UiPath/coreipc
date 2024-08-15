using System.Net.WebSockets;

namespace UiPath.Ipc.Transport.WebSocket;

public sealed record WebSocketTransport : ClientTransport
{
    public required Uri Uri { get; init; }
    public override string ToString() => $"WebSocketClient={Uri}";

    public override IClientState CreateState() => new WebSocketClientState();

    public override void Validate()
    {
        if (Uri is null)
        {
            throw new InvalidOperationException($"{nameof(Uri)} is required.");
        }
    }
}

internal sealed class WebSocketClientState : IClientState
{
    private ClientWebSocket? _clientWebSocket;

    public Network? Network { get; private set; }

    public bool IsConnected() => _clientWebSocket?.State is WebSocketState.Open;

    public async ValueTask Connect(IpcClient client, CancellationToken ct)
    {
        var transport = client.Transport as WebSocketTransport ?? throw new InvalidOperationException();

        _clientWebSocket = new();
        await _clientWebSocket.ConnectAsync(transport.Uri, ct);
        Network = new WebSocketStream(_clientWebSocket);
    }

    public void Dispose() => _clientWebSocket?.Dispose();
}
