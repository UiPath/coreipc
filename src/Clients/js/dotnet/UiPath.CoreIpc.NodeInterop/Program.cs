using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UiPath.Ipc.Transport.NamedPipe;
using UiPath.Ipc.Transport.WebSocket;

namespace UiPath.Ipc.NodeInterop;

using static Contracts;
using static ServiceImpls;
using static Signalling;
using static UiPath.Ipc.NodeInterop.Extensions;

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
        if ((pipe, websocket) is (null, null))
        {
            Console.Error.WriteLine($"Expecting either a non-null pipe name or a non-null websocket url or both.");
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
        if (pipeName is null && webSocketUrl is null)
        {
            throw new ArgumentException($"At least one of {nameof(pipeName)} or {nameof(webSocketUrl)} must be specified.");
        }

        if (maybeSecondsPowerOnDelay is { } secondsPowerOnDelay)
        {
            await Task.Delay(TimeSpan.FromSeconds(secondsPowerOnDelay));
        }

        Send(SignalKind.PoweringOn);
        var services = new ServiceCollection();

        var sp = services
            .AddLogging()
            .AddSingleton<IAlgebra, Algebra>()
            .AddSingleton<ICalculus, Calculus>()
            .AddSingleton<IBrittleService, BrittleService>()
            .AddSingleton<IEnvironmentVariableGetter, EnvironmentVariableGetter>()
            .AddSingleton<IDtoService, DtoService>()
            .BuildServiceProvider();

        var thread = new AsyncContextThread();
        thread.Context.SynchronizationContext.Send(_ => Thread.CurrentThread.Name = "GuiThread", null);
        var scheduler = thread.Context.Scheduler;

        var ipcServers = EnumerateServerTransports(pipeName, webSocketUrl)
            .Select(CreateAndStartIpcServer)
            .ToArray();

        _ = Task.Run(async () =>
        {
            try
            {
                await using var sp = new ServiceCollection()
                    .AddLogging()
                    .BuildServiceProvider();

                var callback = new Arithmetic();

                IEnumerable<Task> EnumeratePings()
                {
                    if (webSocketUrl is not null)
                    {
                        yield return new IpcClient
                        {
                            ServiceProvider = sp,
                            RequestTimeout = TimeSpan.FromHours(5),
                            Callbacks = new()
                                {
                                    { typeof(IArithmetic), callback }
                                },
                            Transport = new WebSocketClientTransport
                            {
                                Uri = new(webSocketUrl),
                            }
                        }
                        .GetProxy<IAlgebra>()
                        .Ping();
                    }

                    if (pipeName is not null)
                    {
                        yield return new IpcClient
                        {
                            ServiceProvider = sp,
                            RequestTimeout = TimeSpan.FromHours(5),
                            Callbacks = new()
                            {
                                { typeof(IArithmetic), callback }
                            },
                            Transport = new NamedPipeClientTransport()
                            {
                                PipeName = pipeName,
                            }
                        }
                        .GetProxy<IAlgebra>()
                        .Ping();
                    }
                }

                await Task.WhenAll(EnumeratePings());

                Send(SignalKind.ReadyToConnect);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                CannotConnect(ex);
            }
        });

        await Task.WhenAll(ipcServers.Select(ipcServer => ipcServer.WaitForStop()));

        IpcServer CreateAndStartIpcServer(ServerTransport transport)
        {
            var ipcServer = new IpcServer()
            {
                Endpoints = new()
                {
                    typeof(IAlgebra),
                    typeof(ICalculus),
                    typeof(IBrittleService),
                    typeof(IEnvironmentVariableGetter),
                    typeof(IDtoService)
                },
                Transport = transport,
                ServiceProvider = sp,
                Scheduler = scheduler
            };
            ipcServer.Start();
            return ipcServer;
        }


        IEnumerable<ServerTransport> EnumerateServerTransports(string? pipeName, string? webSocketUrl)
        {
            if (pipeName is not null)
            {
                yield return new NamedPipeServerTransport() { PipeName = pipeName };
            }

            if (webSocketUrl is not null)
            {
                string url = CurateWebSocketUrl(webSocketUrl);
                var accept = new HttpSysWebSocketsListener(url).Accept;
                yield return new WebSocketServerTransport() { Accept = accept };
            }

            static string CurateWebSocketUrl(string raw)
            {
                var builder = new UriBuilder(raw);
                builder.Scheme = "http";
                return builder.ToString();
            }
        }
    }

    private class Arithmetic : IArithmetic
    {
        public Task<int> Sum(int x, int y) => Task.FromResult(x + y);

        public Task<bool> SendMessage(Message<int> message) => Task.FromResult(true);
    }
}

internal static class Extensions
{
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
