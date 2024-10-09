#if NET461

namespace System.Net.Sockets;

internal static class TcpClientExtensions
{
    public static async Task ConnectAsync(this TcpClient tcpClient, IPAddress address, int port, CancellationToken cancellationToken)
    {
        using var token = cancellationToken.Register(state => (state as TcpClient)!.Dispose(), tcpClient);
        await tcpClient.ConnectAsync(address, port);
    }
}

#endif