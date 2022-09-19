using System.Net.WebSockets;
namespace UiPath.CoreIpc.WebSockets;
using Accept = Func<CancellationToken, Task<WebSocket>>;
public class WebSocketSettings : ListenerSettings
{
    public WebSocketSettings(Accept accept) : base("") => Accept = accept;
    public Accept Accept { get; }
}
class WebSocketListener : Listener
{
    public WebSocketListener(ListenerSettings settings) : base(settings){}
    protected override ServerConnection CreateServerConnection() => new WebSocketConnection(this);
    class WebSocketConnection : ServerConnection
    {
        WebSocketStream _webSocketStream;
        public WebSocketConnection(Listener listener) : base(listener){}
        public override async Task AcceptClient(CancellationToken cancellationToken) =>
            _webSocketStream = new(await ((WebSocketSettings)_listener.Settings).Accept(cancellationToken));
        protected override void Dispose(bool disposing)
        {
            _webSocketStream?.Dispose();
            base.Dispose(disposing);
        }
        protected override Stream Network => _webSocketStream;
    }
}
public static class WebSocketServiceExtensions
{
    public static ServiceHostBuilder UseWebSockets(this ServiceHostBuilder builder, WebSocketSettings settings) => 
        builder.AddListener(new WebSocketListener(settings));
}