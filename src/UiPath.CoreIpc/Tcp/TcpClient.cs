using System.Net.Sockets;

namespace UiPath.Ipc.Tcp;

class TcpClient<TInterface> : ServiceClient<TInterface> where TInterface : class
{
    private readonly TcpConnectionKey _key;

    public TcpClient(TcpConnectionKey key, ConnectionConfig config) : base(config, key)
    => _key = key;

    public override string DebugName => base.DebugName ?? _key.EndPoint.ToString();
}

internal sealed class TcpClientConnection : ClientConnection
{
    private readonly TcpConnectionKey _key;
    private TcpClient? _tcpClient;

    public TcpClientConnection(TcpConnectionKey key) : base(key)
    {
        _key = key;
    }

    public override bool Connected => _tcpClient is { Client: { Connected: true } };

    protected override void Dispose(bool disposing)
    {
        _tcpClient?.Dispose();
        base.Dispose(disposing);
    }

    public override async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        _tcpClient = new();        
        await _tcpClient.ConnectAsync(_key.EndPoint.Address, _key.EndPoint.Port, cancellationToken);
        return _tcpClient.GetStream();
    }
}
