using UiPath.Ipc.Transport.WebSocket;

namespace UiPath.Ipc.Tests;

public sealed class SystemTestsOverWebSockets : SystemTests
{
    private readonly Uri _wsUri;
    private readonly Uri _httpUri;
    private readonly HttpSysWebSocketsListener _httpListener;

    public SystemTestsOverWebSockets()
    {
        NetworkHelper.FindFreeLocalPort(out _wsUri, out _httpUri);
        _httpListener = new HttpSysWebSocketsListener(_httpUri.ToString());
    }

    protected override async Task DisposeAsync()
    {
        await _httpListener.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override ListenerConfig CreateListener() => CommonConfigListener(new WebSocketListener()
    {
        Accept = _httpListener.Accept,
        RequestTimeout = Debugger.IsAttached ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(2),
        MaxReceivedMessageSizeInMegabytes = 1,
    });

    protected override ClientBase CreateClient() => CommonConfigClient(new WebSocketClient()
    {
        Uri = _wsUri,
    });

}
