﻿using System.Net.WebSockets;
namespace UiPath.Rpc.WebSockets;
using ConnectionFactory = Func<Connection, Connection>;
using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;
interface IWebSocketsKey : IConnectionKey
{
    Uri Uri { get; }
}
class WebSocketClient<TInterface> : ServiceClient<TInterface>, IWebSocketsKey where TInterface : class
{
    public WebSocketClient(Uri uri, TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, BeforeCallHandler beforeCall, EndpointSettings serviceEndpoint) : base(requestTimeout, logger, connectionFactory, beforeCall, serviceEndpoint)
    {
        Uri = uri;
        HashCode = uri.GetHashCode();
    }
    public override string Name => base.Name ?? Uri.ToString();
    public Uri Uri { get; }
    public override bool Equals(IConnectionKey other) => other == this || (other is IWebSocketsKey otherClient && Uri.Equals(otherClient.Uri) && base.Equals(other));
    public override ClientConnection CreateClientConnection() => new WebSocketClientConnection(this);
    class WebSocketClientConnection : ClientConnection
    {
        ClientWebSocket _clientWebSocket;
        public WebSocketClientConnection(IConnectionKey connectionKey) : base(connectionKey) {}
        public override bool Connected => _clientWebSocket?.State == WebSocketState.Open;
        protected override void Dispose(bool disposing)
        {
            _clientWebSocket?.Dispose();
            base.Dispose(disposing);
        }
        public override async Task<Stream> Connect(CancellationToken cancellationToken)
        {
            _clientWebSocket = new();
            var uri = ((IWebSocketsKey)ConnectionKey).Uri;
            await _clientWebSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            return new WebSocketStream(_clientWebSocket);
        }
    }
}