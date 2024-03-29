﻿using System.Net.WebSockets;
namespace UiPath.Rpc.WebSockets;
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
        public WebSocketConnection(Listener listener) : base(listener){}
        public override async Task<Stream> AcceptClient(CancellationToken cancellationToken) => 
            new WebSocketStream(await ((WebSocketSettings)_listener.Settings).Accept(cancellationToken).ConfigureAwait(false));
    }
}
public static class WebSocketServiceExtensions
{
    public static ServiceHostBuilder UseWebSockets(this ServiceHostBuilder builder, WebSocketSettings settings) => 
        builder.AddListener(new WebSocketListener(settings));
}