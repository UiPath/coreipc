using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using UiPath.Ipc.Transport.WebSocket;
namespace UiPath.Ipc.Tests;
class WebSocketServer
{
    public static async Task _Main()
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
        //GuiLikeSyncContext.Install();
        Console.WriteLine(SynchronizationContext.Current);
        var serviceProvider = ConfigureServices();
        // build and run service host
        //var data = File.ReadAllBytes(@"../../../../localhost.pfx");

        await using var ipcServer = new IpcServer
        {
            Transport = new WebSocketServerTransport
            {
                Accept = new HttpSysWebSocketsListener("http://localhost:1212/wsDemo/").Accept,
            },
            ServiceProvider = serviceProvider,
            Endpoints = new()
            {
                typeof(IComputingService),
                typeof(ISystemService)
            },
            RequestTimeout = TimeSpan.FromSeconds(2),
        };

        Console.WriteLine(typeof(int).Assembly);

        ipcServer.Start();
        await ipcServer.WaitForStart();
        Console.WriteLine("Server started.");

        // console cancellationtoken
        var tcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult(null);
        };
        await tcs.Task;
        await ipcServer.DisposeAsync();

        Console.WriteLine("Server stopped.");
        return;
    }
    private static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<IComputingService, ComputingService>()
            .AddSingleton<ISystemService, SystemService>()
            .BuildServiceProvider();
}