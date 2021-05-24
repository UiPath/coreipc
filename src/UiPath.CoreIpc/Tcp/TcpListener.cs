using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TcpServer = System.Net.Sockets.TcpListener;
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
        readonly TcpServer _tcpServer;
        public TcpListener(ListenerSettings settings) : base(settings)
        {
            _tcpServer = new TcpServer(Settings.EndPoint);
            _tcpServer.Start(backlog: Settings.ConcurrentAccepts);
        }
        public new TcpSettings Settings => (TcpSettings)base.Settings;
        protected async override Task AcceptConnection(CancellationToken token)
        {
            System.Net.Sockets.TcpClient tcpClient = null;
            try
            {
                using var closeToken = token.Register(Dispose);
                tcpClient = await _tcpServer.AcceptTcpClientAsync();
                // pass the ownership of the connection
                HandleConnection(tcpClient.GetStream(), callbackFactory => new Client(action => action(), callbackFactory), token);
            }
            catch (Exception ex)
            {
                tcpClient?.Dispose();
                if (!token.IsCancellationRequested)
                {
                    Logger.LogException(ex, Settings.Name);
                }
            }
        }
        void Dispose() => _tcpServer.Server?.Dispose();
    }
    public static class TcpServiceExtensions
    {
        public static ServiceHostBuilder UseTcp(this ServiceHostBuilder builder, TcpSettings settings)
        {
            settings.ServiceProvider = builder.ServiceProvider;
            settings.Endpoints = builder.Endpoints;
            builder.AddListener(new TcpListener(settings));
            return builder;
        }
    }
}