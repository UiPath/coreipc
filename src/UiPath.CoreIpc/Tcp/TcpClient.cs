using Microsoft.Extensions.Logging;
using System;
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
        private TcpClient _tcpClient;
        public TcpClient(IPEndPoint endPoint, ISerializer serializer, TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, bool encryptAndSign, BeforeCallHandler beforeCall, EndpointSettings serviceEndpoint) : base(serializer, requestTimeout, logger, connectionFactory, encryptAndSign, beforeCall, serviceEndpoint)
        {
            EndPoint = endPoint;
        }

        public IPEndPoint EndPoint { get; }
        public override int GetHashCode() => EndPoint.GetHashCode();
        bool IEquatable<IConnectionKey>.Equals(IConnectionKey other) => other == this || (other is ITcpKey otherClient && EndPoint.Equals(otherClient.EndPoint));
        protected async override Task<bool> ConnectToServerAsync(CancellationToken cancellationToken)
        {
            if (_tcpClient?.Connected == true)
            {
                return false;
            }
            using var connectionHandle = await ClientConnectionsRegistry.GetOrCreate(this, cancellationToken);
            var clientConnection = connectionHandle.ClientConnection;
            var tcpClient = (TcpClient)clientConnection.State;
            if (tcpClient != null)
            {
                if (tcpClient.Connected)
                {
                    ReuseClientConnection(clientConnection);
                    _tcpClient = tcpClient;
                    return false;
                }
                tcpClient.Dispose();
            }
            tcpClient = new();
            try
            {
                using var token = cancellationToken.Register(tcpClient.Dispose);
                await tcpClient.ConnectAsync(EndPoint.Address, EndPoint.Port);
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
            await CreateClientConnection(clientConnection, tcpClient.GetStream(), tcpClient, EndPoint.ToString());
            _tcpClient = tcpClient;
            return true;
        }
    }
}