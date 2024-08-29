using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using UiPath.Ipc.BackCompat;
using UiPath.Ipc.Transport.WebSocket;
namespace UiPath.Ipc.Tests;
class WebSocketServer
{
    //private static readonly Timer _timer = new Timer(_ =>
    //{
    //    Console.WriteLine("GC.Collect");
    //    GC.Collect();
    //    GC.WaitForPendingFinalizers();
    //    GC.Collect();
    //}, null, 0, 3000);

    static async Task _Main()
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
        //GuiLikeSyncContext.Install();
        Console.WriteLine(SynchronizationContext.Current);
        var serviceProvider = ConfigureServices();
        // build and run service host
        //var data = File.ReadAllBytes(@"../../../../localhost.pfx");
        var host = new ServiceHostBuilder(serviceProvider)
            .UseWebSockets(new WebSocketListener()
            {
                Accept = new HttpSysWebSocketsListener("http://localhost:1212/wsDemo/").Accept,
                RequestTimeout = TimeSpan.FromSeconds(2),
                //Certificate = new X509Certificate(data, "1"),
            })
            .AddEndpoint<IComputingService>()
            .AddEndpoint<ISystemService>()
            .ValidateAndBuild();
        await await Task.WhenAny(host.RunAsync(), Task.Run(async () =>
        {
            Console.WriteLine(typeof(int).Assembly);
            Console.ReadLine();
            await host.DisposeAsync();
        }));
        Console.WriteLine("Server stopped.");
        return;
    }
    private static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddIpcWithLogging()
            .AddSingleton<IComputingService, ComputingService>()
            .AddSingleton<ISystemService, SystemService>()
            .BuildServiceProvider();
}