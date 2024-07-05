namespace UiPath.Ipc.Tcp;

internal class TcpListener : Listener
{
    private readonly System.Net.Sockets.TcpListener _tcpServer;

    public new TcpListenerConfig Config { get; }

    public TcpListener(IpcServer server, TcpListenerConfig config) : base(server, config)
    {
        Config = config;
        _tcpServer = new(Config.EndPoint);
        _tcpServer.Start(backlog: Config.ConcurrentAccepts);

        EnsureListening();
    }

    protected override ServerConnection CreateServerConnection() => new TcpServerConnection(this);

    protected override async Task DisposeCore()
    {
        await base.DisposeCore();
        _tcpServer.Stop();
    }

    private Task<System.Net.Sockets.TcpClient> AcceptClient(CancellationToken cancellationToken) => _tcpServer.AcceptTcpClientAsync();

    private sealed class TcpServerConnection : ServerConnection
    {
        private new readonly TcpListener _listener;
        private System.Net.Sockets.TcpClient? _tcpClient;

        public TcpServerConnection(TcpListener listener) : base(listener)
        {
            _listener = listener;
        }

        public override async Task<Stream> AcceptClient(CancellationToken cancellationToken)
        {
            _tcpClient = await _listener.AcceptClient(cancellationToken);
            return _tcpClient.GetStream();
        }

        protected override void Dispose(bool disposing)
        {
            _tcpClient?.Dispose();
            base.Dispose(disposing);
        }
    }
}
