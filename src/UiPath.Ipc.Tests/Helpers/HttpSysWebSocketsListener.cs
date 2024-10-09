using System.Net.WebSockets;
using System.Net;
using System.Threading.Channels;

namespace UiPath.Ipc.Tests;

public class HttpSysWebSocketsListener : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly HttpListener _httpListener = new();
    private readonly Channel<HttpListenerContext> _channel = Channel.CreateBounded<HttpListenerContext>(capacity: 5);
    private readonly Task _processingContexts;

    public HttpSysWebSocketsListener(string uriPrefix)
    {
        _httpListener.Prefixes.Add(uriPrefix);
        _httpListener.Start();

        _processingContexts = ProcessContexts(_cts.Token);
    }

    private async Task ProcessContexts(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var context = await _httpListener.GetContextAsync();
                await _channel.Writer.WriteAsync(context, ct);
            }
            _channel.Writer.Complete();
        }
        catch (Exception ex)
        {
            _channel.Writer.Complete(ex);
        }
    }

    public async Task<WebSocket> Accept(CancellationToken ct)
    {
        while (true)
        {
            var listenerContext = await _channel.Reader.ReadAsync(ct);
            if (listenerContext.Request.IsWebSocketRequest)
            {
                var webSocketContext = await listenerContext.AcceptWebSocketAsync(subProtocol: null);
                return webSocketContext.WebSocket;
            }
            listenerContext.Response.StatusCode = 400;
            listenerContext.Response.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _httpListener.Stop();

        try
        {
            await _processingContexts;
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == _cts.Token)
        {
            // ignore
        }
    }
}