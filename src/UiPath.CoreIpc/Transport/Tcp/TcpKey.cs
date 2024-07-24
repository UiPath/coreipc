using System.Net;
using System.Net.Sockets;

namespace UiPath.Ipc.Transport.Tcp;

using ITcpKey = IConnectionKey<TcpKey, TcpClientConnectionState>;

public sealed record TcpKey : ITcpKey
{
    public required IPEndPoint EndPoint { get; init; }

    public required ClientBase DefaultConfig { get; init; }

    async Task<OneOf<IAsyncStream, Stream>> ITcpKey.Connect(CancellationToken ct)
    {
        var client = new TcpClient();
        await client.ConnectAsync(EndPoint.Address, EndPoint.Port, ct);
        return client.GetStream();
    }
}

internal sealed class TcpClientConnectionState
{
    public required TcpClient Client { get; init; }
}
