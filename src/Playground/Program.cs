using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playground;
using UiPath.Ipc;
using UiPath.Ipc.Transport.NamedPipe;

internal class Program
{
    private static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Uri serverUri = new("http://localhost:62234");
        Uri clientUri = new("http://localhost:62235");

        var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);

        var serverScheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;
        var clientScheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;

        await using var serverSP = new ServiceCollection()
            .AddSingleton<Impl.ClientRegistry>()
            .AddScoped<Contracts.IServerOperations, Impl.Server>()
            .AddLogging(builder => builder.AddConsole())
            .BuildServiceProvider();

        await using var clientSP = new ServiceCollection()
            .AddScoped<Contracts.IClientOperations, Impl.ClientOperations>()
            .AddLogging(builder => builder.AddConsole())
            .BuildServiceProvider();

        await using var ipcServer = new IpcServer()
        {
            Scheduler = serverScheduler,
            ServiceProvider = serverSP,
            RequestTimeout = TimeSpan.FromHours(10),
            Endpoints = new()
            {
                typeof(Contracts.IServerOperations), // DEVINE
                new EndpointSettings(typeof(Contracts.IServerOperations)) // ASTALALT
                {
                    BeforeIncomingCall = async (callInfo, _) =>
                    {
                        Console.WriteLine($"Server: {callInfo.Method.Name}");
                    }
                },
                typeof(Contracts.IClientOperations2)
            },
            Transport = new NamedPipeServerTransport()
            {
                PipeName = Contracts.PipeName,
                ServerName = ".",
                AccessControl = ps =>
                {
                },
                MaxReceivedMessageSizeInMegabytes = 100,
            }
        };

        try
        {
            ipcServer.Start(); // ar putea fi void, ar putea fi si Run
            // await ipcServer.WaitForStart();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to start.");
            Console.WriteLine(ex.ToString());
            throw;
        }

        var c1 = new IpcClient()
        {
            Callbacks = new()
            {
                typeof(Contracts.IClientOperations),
                { typeof(Contracts.IClientOperations2), new Impl.Client2() },
            },
            ServiceProvider = clientSP,
            Scheduler = clientScheduler,
            Transport = new NamedPipeClientTransport()
            {
                PipeName = Contracts.PipeName,
                ServerName = ".",
                AllowImpersonation = false,
            },
            DebugName = "Client1",
        };

        var c2 = new IpcClient()
        {
            ServiceProvider = clientSP,
            Callbacks = new()
            {
                typeof(Contracts.IClientOperations),
                { typeof(Contracts.IClientOperations2), new Impl.Client2() },
            },
            Scheduler = clientScheduler,
            Transport = new NamedPipeClientTransport()
            {
                PipeName = Contracts.PipeName,
                ServerName = ".",
                AllowImpersonation = false,
            },
        };

        var proxy1 = new IpcClient()
        {
            ServiceProvider = clientSP,
            Callbacks = new()
            {
                typeof(Contracts.IClientOperations),
                { typeof(Contracts.IClientOperations2), new Impl.Client2() },
            },
            Scheduler = clientScheduler,
            Transport = new NamedPipeClientTransport()
            {
                PipeName = Contracts.PipeName,
                ServerName = ".",
                AllowImpersonation = false,
            },
        }.GetProxy<Contracts.IServerOperations>();


        await proxy1.Register();
        await proxy1.Broadcast("Hello Bidirectional Http!");

        await Task.WhenAny(cancelled);
    }
}