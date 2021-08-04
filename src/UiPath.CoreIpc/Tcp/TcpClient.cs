using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.Tcp
{
    using ConnectionFactory = Func<Connection, CancellationToken, Task<Connection>>;
    using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;
    interface ITcpKey : IConnectionKey
    {
        IPEndPoint EndPoint { get; }
    }
    class TcpClient<TInterface> : ServiceClient<TInterface>, ITcpKey where TInterface : class
    {
        public TcpClient(IPEndPoint endPoint, ISerializer serializer, TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, bool encryptAndSign, BeforeCallHandler beforeCall, EndpointSettings serviceEndpoint) : base(serializer, requestTimeout, logger, connectionFactory, encryptAndSign, beforeCall, serviceEndpoint) =>
            EndPoint = endPoint;
        public IPEndPoint EndPoint { get; }
        public override int GetHashCode() => EndPoint.GetHashCode();
        public override bool Equals(IConnectionKey other) => other == this || (other is ITcpKey otherClient && EndPoint.Equals(otherClient.EndPoint));
        public override ClientConnection CreateClientConnection(IConnectionKey key) => new TcpClientConnection(key);
        class TcpClientConnection : ClientConnection
        {
            private TcpClient _tcpClient;
            public TcpClientConnection(IConnectionKey connectionKey) : base(connectionKey) {}
            public override bool Connected => _tcpClient?.Client?.Connected is true;
            protected override void Dispose(bool disposing)
            {
                _tcpClient?.Dispose();
                base.Dispose(disposing);
            }
            public override Stream Network => _tcpClient.GetStream();
            public override async Task ConnectAsync(CancellationToken cancellationToken)
            {
                var endPoint = ((ITcpKey)ConnectionKey).EndPoint;
                _tcpClient = new();
                using var token = cancellationToken.Register(Dispose);
                await _tcpClient.ConnectAsync(endPoint.Address, endPoint.Port);
            }
        }
    }
}