using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
namespace UiPath.CoreIpc.Tcp
{
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
        async Task<System.Net.Sockets.TcpClient> AcceptClient(CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(Dispose))
            {
                return await _tcpServer.AcceptTcpClientAsync();
            }
        }
        class TcpServerConnection : ServerConnection
        {
            System.Net.Sockets.TcpClient _tcpClient;
            public TcpServerConnection(Listener listener) : base(listener){}
            public override async Task AcceptClient(CancellationToken cancellationToken) =>
                _tcpClient = await ((TcpListener)_listener).AcceptClient(cancellationToken);
            protected override void Dispose(bool disposing)
            {
                _tcpClient?.Dispose();
                base.Dispose(disposing);
            }
            protected override Stream Network => _tcpClient.GetStream();
        }
    }
    public static class TcpServiceExtensions
    {
        public static ServiceHostBuilder UseTcp(this ServiceHostBuilder builder, TcpSettings settings)
        {
            settings.ServiceProvider = builder.ServiceProvider;
            settings.Endpoints = builder.Endpoints;
            return builder.AddListener(new TcpListener(settings));
        }
    }
}