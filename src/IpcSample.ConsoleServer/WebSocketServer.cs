using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using UiPath.CoreIpc.WebSockets;
namespace UiPath.CoreIpc.Tests;
class WebSocketServer
{
    //private static readonly Timer _timer = new Timer(_ =>
    //{
    //    Console.WriteLine("GC.Collect");
    //    GC.Collect();
    //    GC.WaitForPendingFinalizers();
    //    GC.Collect();
    //}, null, 0, 3000);

    static async Task Main()
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
        //GuiLikeSyncContext.Install();
        Console.WriteLine(SynchronizationContext.Current);
        var serviceProvider = ConfigureServices();
        // build and run service host
        //var data = File.ReadAllBytes(@"../../../../localhost.pfx");
        var host = new ServiceHostBuilder(serviceProvider)
            .UseWebSockets(new(new HttpSysWebSocketsListener("http://localhost:1212/wsDemo/").Accept)
            {
                RequestTimeout = TimeSpan.FromSeconds(2),
                //Certificate = new X509Certificate(data, "1"),
            })
            .AddEndpoint<IComputingService, IComputingCallback>()
            .AddEndpoint<ISystemService>()
            .ValidateAndBuild();
        await await Task.WhenAny(host.RunAsync(), Task.Run(() =>
        {
            Console.WriteLine(typeof(int).Assembly);
            Console.ReadLine();
            host.Dispose();
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