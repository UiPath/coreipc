using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Net;
using UiPath.Ipc.Tcp;

namespace UiPath.Ipc.Tests;

class TcpServer
{
    static readonly IPEndPoint SystemEndPoint = new(IPAddress.Any, 3131);
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
        var data = File.ReadAllBytes(@"../../../../localhost.pfx");
        var host = new ServiceHostBuilder(serviceProvider)
            .UseTcp(new TcpSettings(SystemEndPoint)
            {
                RequestTimeout = TimeSpan.FromSeconds(2),
                //Certificate = new X509Certificate(data, "1"),
            })
            .AddEndpoint<IComputingService>()
            .AddEndpoint<ISystemService>()
            .AllowCallback(typeof(IComputingCallback))
            .ValidateAndBuild();

        await await Task.WhenAny(host.RunAsync(), Task.Run(() =>
        {
            Console.WriteLine(typeof(int).Assembly);
            Console.ReadLine();
            host.Dispose();
        }));

        Console.WriteLine("Server stopped.");
    }

    private static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddIpcWithLogging()
            .AddSingleton<IComputingService, ComputingService>()
            .AddSingleton<ISystemService, SystemService>()
            .BuildServiceProvider();
}