using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using UiPath.Ipc.BackCompat;
using UiPath.Ipc.Tests;

namespace UiPath.Ipc;

public static class IpcHelpers
{
    public static TInterface ValidateAndBuild<TDerived, TInterface>(this ServiceClientBuilder<TDerived, TInterface> builder) where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
    {
#if DEBUG
        BackCompatValidator.Validate(builder);
#endif
        return builder.Build();
    }
    public static ServiceHost ValidateAndBuild(this ServiceHostBuilder serviceHostBuilder)
    {
#if DEBUG
        BackCompatValidator.Validate(serviceHostBuilder);
#endif
        return serviceHostBuilder.Build();
    }
    public static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddLogging(b => b.AddTraceSource(new SourceSwitch("", "All")))
            .AddIpc()
            .AddSingleton<IComputingServiceBase, ComputingService>()
            .AddSingleton<IComputingService, ComputingService>()
            .AddSingleton<ISystemService, SystemService>()
            .BuildServiceProvider();
    public static string GetUserName(this IClient client)
    {
        string userName = null;
        client.Impersonate(() => userName = Environment.UserName);
        return userName;
    }
    public static IServiceCollection AddIpcWithLogging(this IServiceCollection services, bool logToConsole = false)
    {
        services.AddLogging(builder =>
        {
            //if (logToConsole)
            //{
            //    builder.AddConsole();
            //}
            //foreach (var listener in Trace.Listeners.Cast<TraceListener>().Where(l => !(l is DefaultTraceListener)))
            //{
            //    builder.AddTraceSource(new SourceSwitch(listener.Name, "All"), listener);
            //}
        });
        return services.AddIpc();
    }
}
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