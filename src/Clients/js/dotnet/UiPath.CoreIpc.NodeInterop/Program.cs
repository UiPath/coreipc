using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UiPath.CoreIpc.NamedPipe;
using UiPath.CoreIpc.WebSockets;

namespace UiPath.CoreIpc.NodeInterop;

using static Contracts;
using static ServiceImpls;
using static Signalling;

class Program
{
    /// <summary>
    /// .NET - Nodejs Interop Helper
    /// </summary>
    /// <param name="pipe">The pipe name on which the CoreIpc endpoints will be hosted at.</param>
    /// <param name="websocket">The websocket url on which the CoreIpc endpoints will be hosted at.</param>
    /// <param name="mutex">Optional process mutual exclusion name.</param>
    /// <param name="delay">Optional number of seconds that the process will wait before it exposes the CoreIpc endpoints.</param>
    static async Task<int> Main(
        string? pipe,
        string? websocket,
        string? mutex = null,
        int? delay = null)
    {
        Debugger.Launch();

        if ((pipe, websocket) is (null, null))
        {
            Console.Error.WriteLine($"Expecting either a non-null pipe name or a non-null websocket url.");
            return 1;
        }

        try
        {
            if (mutex is { })
            {
                using var _ = new Mutex(initiallyOwned: false, mutex, out bool createdNew);
                if (!createdNew) { return 2; }
                await MainCore(pipe, websocket, delay);
            }
            else
            {
                await MainCore(pipe, websocket, delay);
            }
        }
        catch (Exception ex)
        {
            Throw(ex);
            throw;
        }

        return 0;
    }

    static async Task MainCore(string? pipeName, string? webSocketUrl, int? maybeSecondsPowerOnDelay)
    {
        if (maybeSecondsPowerOnDelay is { } secondsPowerOnDelay)
        {
            await Task.Delay(TimeSpan.FromSeconds(secondsPowerOnDelay));
        }

        Send(SignalKind.PoweringOn);
        var services = new ServiceCollection();

        var sp = services
            .AddLogging()
            .AddIpc()
            .AddSingleton<IAlgebra, Algebra>()
            .AddSingleton<ICalculus, Calculus>()
            .AddSingleton<IBrittleService, BrittleService>()
            .AddSingleton<IEnvironmentVariableGetter, EnvironmentVariableGetter>()
            .AddSingleton<IDtoService, DtoService>()
            .BuildServiceProvider();

        var serviceHost = new ServiceHostBuilder(sp)
            .UseNamedPipesOrWebSockets(pipeName, webSocketUrl)
            .AddEndpoint<IAlgebra, IArithmetics>()
            .AddEndpoint<ICalculus>()
            .AddEndpoint<IBrittleService>()
            .AddEndpoint<IEnvironmentVariableGetter>()
            .AddEndpoint<IDtoService>()
            .Build();

        var thread = new AsyncContextThread();
        thread.Context.SynchronizationContext.Send(_ => Thread.CurrentThread.Name = "GuiThread", null);
        var sched = thread.Context.Scheduler;

        _ = Task.Run(async () =>
        {
            try
            {
                await new NamedPipeClientBuilder<IAlgebra>(pipeName)
                    .RequestTimeout(TimeSpan.FromSeconds(2))
                    .Build()
                    .Ping();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            Send(SignalKind.ReadyToConnect);
        });

        await serviceHost.RunAsync(sched);
    }
}

internal static class Extensions
{
    public static ServiceHostBuilder UseNamedPipesOrWebSockets(this ServiceHostBuilder builder, string? pipeName, string? webSocketUrl)
    {
        if (pipeName is not null)
        {
            return builder.UseNamedPipes(new NamedPipeSettings(pipeName));
        }

        if (webSocketUrl is not null)
        {
            string url = CurateWebSocketUrl(webSocketUrl);
            var accept = new HttpSysWebSocketsListener(url).Accept;
            WebSocketSettings settings = new(accept);

            return builder.UseWebSockets(settings);
        }

        throw new ArgumentOutOfRangeException();
    }

    private static string CurateWebSocketUrl(string raw)
    {
        var builder = new UriBuilder(raw);
        builder.Scheme = "http";
        return builder.ToString();
    }

    public class HttpSysWebSocketsListener : IDisposable
    {
        HttpListener _httpListener = new();
        public HttpSysWebSocketsListener(string uriPrefix)
        {
            _httpListener.Prefixes.Add(uriPrefix);
            _httpListener.Start();
        }
        public async Task<WebSocket> Accept(CancellationToken token)
        {
            while (true)
            {
                var listenerContext = await _httpListener.GetContextAsync();
                if (listenerContext.Request.IsWebSocketRequest)
                {
                    var webSocketContext = await listenerContext.AcceptWebSocketAsync(subProtocol: null);
                    return webSocketContext.WebSocket;
                }
                listenerContext.Response.StatusCode = 400;
                listenerContext.Response.Close();
            }
        }
        public void Dispose() => _httpListener.Stop();
    }
}
