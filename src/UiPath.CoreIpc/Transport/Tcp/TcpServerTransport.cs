using System.Net;
using System.Net.Sockets;

namespace UiPath.Ipc.Transport.Tcp;

public sealed class TcpServerTransport : ServerTransport
{
    public required IPEndPoint EndPoint { get; init; }

    internal override IServerState CreateServerState()
    {
        var listener = new TcpListener(EndPoint);
        listener.Start(backlog: ConcurrentAccepts);
        return new ServerState() { TcpListener = listener };
    }

    internal override IEnumerable<string?> ValidateCore()
    {
        yield return IsNotNull(EndPoint);
    }

    public override string ToString() => $"TcpServer={EndPoint}";

    private sealed class ServerState : IServerState
    {
        public required TcpListener TcpListener { get; init; }

        ValueTask IAsyncDisposable.DisposeAsync()
        {
            TcpListener.Stop();
            return default;
        }

        IServerConnectionSlot IServerState.CreateConnectionSlot()
        => new ServerConnectionState { ServerState = this };
    }

    private sealed class ServerConnectionState : IServerConnectionSlot
    {
        public required ServerState ServerState { get; init; }

        async ValueTask<Stream> IServerConnectionSlot.AwaitConnection(CancellationToken ct)
        {
            TcpClient tcpClient;
#if NET461
            using var ctreg = ct.Register(ServerState.TcpListener.Stop);
            tcpClient = await ServerState.TcpListener.AcceptTcpClientAsync();
#else
            tcpClient = await ServerState.TcpListener.AcceptTcpClientAsync(ct);
#endif
            return tcpClient.GetStream();
        }

        ValueTask IAsyncDisposable.DisposeAsync() => default;
    }
}
