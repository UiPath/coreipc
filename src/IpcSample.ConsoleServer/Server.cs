using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

internal static class Server
{
    public static async Task Main()
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
        //GuiLikeSyncContext.Install();
        Console.WriteLine(SynchronizationContext.Current);
        var serviceProvider = ConfigureServices();
        // build and run service host

        await using var ipcServer = new IpcServer
        {
            Transport = new NamedPipeServerTransport { PipeName = "test" },
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