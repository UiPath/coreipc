using System.Net;
using System.Net.Sockets;

namespace UiPath.Ipc.Transport.Tcp;

using ITcpListenerConfig = IListenerConfig<TcpListener, TcpListenerState, TcpServerConnectionState>;

public sealed record TcpListener : ServerTransport, ITcpListenerConfig
{
    public required IPEndPoint EndPoint { get; init; }

    TcpListenerState ITcpListenerConfig.CreateListenerState(IpcServer server)
    {
        var listener = new System.Net.Sockets.TcpListener(EndPoint);
        listener.Start(backlog: ConcurrentAccepts);

        return new() { Listener = listener };
    }

    TcpServerConnectionState ITcpListenerConfig.CreateConnectionState(IpcServer server, TcpListenerState listenerState)
    => new();

    async ValueTask<Stream> ITcpListenerConfig.AwaitConnection(TcpListenerState listenerState, TcpServerConnectionState connectionState, CancellationToken ct)
    {
        System.Net.Sockets.TcpClient tcpClient;
#if NET461
        using var ctreg = ct.Register(listenerState.Listener.Stop);
        tcpClient = await listenerState.Listener.AcceptTcpClientAsync();
#else
        tcpClient = await listenerState.Listener.AcceptTcpClientAsync(ct);
#endif
        return tcpClient.GetStream();
    }

    IEnumerable<string> ITcpListenerConfig.Validate()
    {
        if (EndPoint is null)
        {
            yield return "EndPoint is required";
        }
    }

    public override string ToString() => $"TcpServer={EndPoint}";
}

internal sealed class TcpListenerState : IAsyncDisposable
{
    public required System.Net.Sockets.TcpListener Listener { get; init; }

    public ValueTask DisposeAsync()
    {
        Listener.Stop();
        return default;
    }
}

internal sealed class TcpServerConnectionState : IDisposable
{
    public void Dispose() { }
}

