namespace UiPath.Ipc.Tcp;

public class TcpListener : Listener<TcpListenerConfig, TcpListener.TcpServerConnection>
{
    private System.Net.Sockets.TcpListener _tcpServer = null!;

    protected internal override void Initialize()
    {
        _tcpServer = new(Config.EndPoint);
        _tcpServer.Start(backlog: Config.ConcurrentAccepts);
    }

    protected override async Task DisposeCore()
    {
        await base.DisposeCore();
        _tcpServer.Stop();
    }

    private Task<System.Net.Sockets.TcpClient> AcceptClient(CancellationToken cancellationToken) => _tcpServer.AcceptTcpClientAsync();

    public sealed class TcpServerConnection : ServerConnection<TcpListener>
    {
        private System.Net.Sockets.TcpClient? _tcpClient;

        public override async Task<Stream> AcceptClient(CancellationToken cancellationToken)
        {
            _tcpClient = await Listener.AcceptClient(cancellationToken);
            return _tcpClient.GetStream();
        }

        protected override void Dispose(bool disposing)
        {
            _tcpClient?.Dispose();
            base.Dispose(disposing);
        }
    }
}
