namespace UiPath.Ipc.Tests;

internal sealed class WebSocketContext : IAsyncDisposable
{
    private readonly HttpSysWebSocketsListener _httpListener;

    public Accept Accept => _httpListener.Accept;
    public Uri ClientUri { get; }

    public WebSocketContext(int? port = null)
    {
        var actualPort = port ?? NetworkHelper.FindFreeLocalPort().Port;
        ClientUri = Uri("ws");
        _httpListener = new(uriPrefix: Uri("http").ToString());

        Uri Uri(string scheme) => new UriBuilder(scheme, "localhost", actualPort).Uri;
    }

    public ValueTask DisposeAsync() => _httpListener.DisposeAsync();
}
