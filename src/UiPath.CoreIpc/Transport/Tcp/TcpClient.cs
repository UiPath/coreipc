using System.Net;

namespace UiPath.Ipc.Transport.Tcp;

public sealed record TcpClient : ClientBase, IClient<TcpClientState, TcpClient>
{
    public required IPEndPoint EndPoint { get; init; }

    public override string ToString() => $"TcpClient={EndPoint}";
}

internal sealed class TcpClientState : IClientState<TcpClient, TcpClientState>
{
    private System.Net.Sockets.TcpClient? _tcpClient;

    public Network? Network { get; private set; }

    public bool IsConnected()
    {
        return _tcpClient?.Client?.Connected is true;
    }

    public async ValueTask Connect(TcpClient client, CancellationToken ct)
    {
        _tcpClient = new System.Net.Sockets.TcpClient();
#if NET461
        using var ctreg = ct.Register(_tcpClient.Dispose);
        try
        {
            await _tcpClient.ConnectAsync(client.EndPoint.Address, client.EndPoint.Port);
        }
        catch (ObjectDisposedException)
        {
            _tcpClient = null;
            throw new OperationCanceledException(ct);
        }
#else
        await _tcpClient.ConnectAsync(client.EndPoint.Address, client.EndPoint.Port, ct);
#endif
        Network = _tcpClient.GetStream();
    }

    public void Dispose() => _tcpClient?.Dispose();
}
