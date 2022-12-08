using System.Net;
namespace UiPath.CoreIpc.Tcp;
public class TcpSettings : ListenerSettings
{
    public TcpSettings(IPEndPoint endPoint) : base(endPoint.ToString())
    {
        EndPoint = endPoint;
    }
    public IPEndPoint EndPoint { get; }
}
class TcpListener : Listener
{
    readonly System.Net.Sockets.TcpListener _tcpServer;
    public TcpListener(ListenerSettings settings) : base(settings)
    {
        _tcpServer = new(Settings.EndPoint);
        _tcpServer.Start(backlog: Settings.ConcurrentAccepts);
    }
    public new TcpSettings Settings => (TcpSettings)base.Settings;
    protected override ServerConnection CreateServerConnection() => new TcpServerConnection(this);
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _tcpServer.Stop();
    }
    Task<System.Net.Sockets.TcpClient> AcceptClient(CancellationToken cancellationToken) => _tcpServer.AcceptTcpClientAsync();
    class TcpServerConnection : ServerConnection
    {
        System.Net.Sockets.TcpClient _tcpClient;
        public TcpServerConnection(Listener listener) : base(listener){}
        public override async Task<Stream> AcceptClient(CancellationToken cancellationToken)
        {
            _tcpClient = await ((TcpListener)_listener).AcceptClient(cancellationToken);
            return _tcpClient.GetStream();
        }
        protected override void Dispose(bool disposing)
        {
            _tcpClient?.Dispose();
            base.Dispose(disposing);
        }
    }
}
public static class TcpServiceExtensions
{
    public static ServiceHostBuilder UseTcp(this ServiceHostBuilder builder, TcpSettings settings) => builder.AddListener(new TcpListener(settings));
}