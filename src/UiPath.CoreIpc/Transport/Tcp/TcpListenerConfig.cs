using System.Net;
using System.Net.Sockets;

namespace UiPath.Ipc.Transport.Tcp;

using ITcpListenerConfig = IListenerConfig<TcpListenerConfig, TcpListenerState, TcpServerConnectionState>;

public sealed record TcpListenerConfig : ListenerConfig, ITcpListenerConfig
{
    public required IPEndPoint EndPoint { get; init; }

    TcpListenerState ITcpListenerConfig.CreateListenerState(IpcServer server)
    {
        var listener = new TcpListener(EndPoint);
        listener.Start(backlog: ConcurrentAccepts);

        return new() { Listener = listener };
    }

    TcpServerConnectionState ITcpListenerConfig.CreateConnectionState(IpcServer server, TcpListenerState listenerState)
    => new();

    async ValueTask<OneOf<IAsyncStream, Stream>> ITcpListenerConfig.AwaitConnection(TcpListenerState listenerState, TcpServerConnectionState connectionState, CancellationToken ct)
    {
        var tcpClient = await listenerState.Listener.AcceptTcpClientAsync();
        return tcpClient.GetStream();
    }

    IEnumerable<string> ITcpListenerConfig.Validate()
    {
        if (EndPoint is null)
        {
            yield return "EndPoint is required";
        }
    }
}

internal sealed class TcpListenerState : IAsyncDisposable
{
    public required TcpListener Listener { get; init; }

    public ValueTask DisposeAsync()
    {
        Listener.Stop();
        return default;
    }
}

internal sealed class TcpServerConnectionState
{
}

