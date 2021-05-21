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
        public TcpListener(ListenerSettings settings) : base(settings){}
        public new TcpSettings Settings => (TcpSettings)base.Settings;
        protected async override Task AcceptConnection(CancellationToken token)
        {
            var server = new TcpServer(Settings.EndPoint);
            using var closeToken = token.Register(Dispose);
            try
            {
                var tcpClient = await server.AcceptTcpClientAsync();
                // pass the ownership of the connection
                HandleConnection(tcpClient.GetStream(), callbackFactory => new Client(action => action(), callbackFactory), token);
            }
            catch (Exception ex)
            {
                Dispose();
                if (!token.IsCancellationRequested)
                {
                    Logger.LogException(ex, Settings.Name);
                }
            }
            return;
            void Dispose() => server.Server?.Dispose();
        }
    }
}