using System.Net.Sockets;

namespace UiPath.Ipc.Tcp;

public sealed class TcpClientConnection : ClientConnection
{
    private TcpConnectionKey Key => ConnectionKey as TcpConnectionKey ?? throw new InvalidOperationException();
    private TcpClient? _tcpClient;

    public override bool Connected => _tcpClient is { Client: { Connected: true } };

    protected override void Dispose(bool disposing)
    {
        _tcpClient?.Dispose();
        base.Dispose(disposing);
    }

    public override async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        _tcpClient = new();        
        await _tcpClient.ConnectAsync(Key.EndPoint.Address, Key.EndPoint.Port, cancellationToken);
        return _tcpClient.GetStream();
    }
}
