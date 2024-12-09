using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using UiPath.Ipc;
using UiPath.Ipc.Transport.Tcp;

namespace UiPath.CoreIpc.Tests;

using IPEndPoint = System.Net.IPEndPoint;
using IPAddress = System.Net.IPAddress;

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

        await using var ipcServer = new IpcServer
        {
            Transport = new TcpServerTransport { EndPoint = SystemEndPoint },
            ServiceProvider = serviceProvider,
            Endpoints = new()
            {
                typeof(IComputingService),
                typeof(ISystemService)
            },
            RequestTimeout = TimeSpan.FromSeconds(2),
        };
        ipcServer.Start();
        await ipcServer.WaitForStart();

        Console.WriteLine("Server started.");

        var tcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult(null);
        };
        await tcs.Task;
        await ipcServer.DisposeAsync();

        Console.WriteLine("Server stopped.");
    }

    private static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<IComputingService, ComputingService>()
            .AddSingleton<ISystemService, SystemService>()
            .BuildServiceProvider();
}