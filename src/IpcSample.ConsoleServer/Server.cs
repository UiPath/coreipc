using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using UiPath.Ipc.BackCompat;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

class Server
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
        var host = new ServiceHostBuilder(serviceProvider)
            .UseNamedPipes(new NamedPipeListener()
            {
                PipeName = "test",
                RequestTimeout = TimeSpan.FromSeconds(2),
                //AccessControl = security => security.AllowCurrentUser(),
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
    }

    private static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddIpcWithLogging()
            .AddSingleton<IComputingService, ComputingService>()
            .AddSingleton<ISystemService, SystemService>()
            .BuildServiceProvider();
}