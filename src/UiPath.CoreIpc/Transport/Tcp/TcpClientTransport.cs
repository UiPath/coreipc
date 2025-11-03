using System.Net;
using System.Net.Sockets;

namespace UiPath.Ipc.Transport.Tcp;

public sealed record TcpClientTransport : ClientTransport
{
    public required IPEndPoint EndPoint { get; set; }

    public override string ToString() => $"TcpClient={EndPoint}";

    internal override IClientState CreateState() => new TcpClientState();

    internal override void Validate()
    {
        if (EndPoint is null)
        {
            throw new InvalidOperationException($"{nameof(EndPoint)} is required.");
        }
    }
}

internal sealed class TcpClientState : IClientState
{
    private TcpClient? _tcpClient;

    public Stream? Network { get; private set; }

    public bool IsConnected()
    {
        return _tcpClient?.Client?.Connected is true;
    }

    public async ValueTask Connect(IpcClient client, CancellationToken ct)
    {
        var transport = client.Transport as TcpClientTransport ?? throw new InvalidOperationException();

        _tcpClient = new();
#if NET461
        using var ctreg = ct.Register(_tcpClient.Dispose);
        try
        {
            await _tcpClient.ConnectAsync(transport.EndPoint.Address, transport.EndPoint.Port);
        }
        catch (ObjectDisposedException)
        {
            _tcpClient = null;
            throw new OperationCanceledException(ct);
        }
#else
        // ported from support/v21.10 (https://github.com/UiPath/coreipc/pull/119)
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _tcpClient.ConnectAsync(transport.EndPoint, ct);
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                await Task.Delay(10, ct);
            }
        }

#endif
        Network = _tcpClient.GetStream();
    }

    public void Dispose() => _tcpClient?.Dispose();
}
