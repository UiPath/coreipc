using System.Net;
using System.Net.Sockets;
namespace UiPath.Rpc.Tcp;
using ConnectionFactory = Func<Connection, Connection>;
using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;
interface ITcpKey : IConnectionKey
{
    IPEndPoint EndPoint { get; }
}
class TcpClient<TInterface> : ServiceClient<TInterface>, ITcpKey where TInterface : class
{
    public TcpClient(IPEndPoint endPoint, TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, BeforeCallHandler beforeCall, EndpointSettings serviceEndpoint) : base(requestTimeout, logger, connectionFactory, beforeCall, serviceEndpoint)
    {
        EndPoint = endPoint;
        HashCode = EndPoint.GetHashCode();
    }
    public override string Name => base.Name ?? EndPoint.ToString();
    public IPEndPoint EndPoint { get; }
    public override bool Equals(IConnectionKey other) => other == this || (other is ITcpKey otherClient && EndPoint.Equals(otherClient.EndPoint) && 
        base.Equals(other));
    public override ClientConnection CreateClientConnection() => new TcpClientConnection(this);
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
        public override async Task<Stream> Connect(CancellationToken cancellationToken)
        {
            _tcpClient = new();
            var endPoint = ((ITcpKey)ConnectionKey).EndPoint;
            await _tcpClient.ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken);
            return _tcpClient.GetStream();
        }
    }
}