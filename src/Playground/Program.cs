using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playground;
using UiPath.CoreIpc.Http;
using UiPath.Ipc;

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

        // IAgentOperations, IAgentEvents
        // asdfasdf
        // asdfasdf

        await using var ipcServer = new IpcServer()
        {
            Scheduler = serverScheduler,
            ServiceProvider = serverSP,
            Endpoints = new()
            {
                typeof(Contracts.IServerOperations),
                typeof(Contracts.IClientOperations2),
                { typeof(Contracts.IClientOperations2), new object() }
            },
            Listeners = [
                new NamedPipeListenerConfig()
                {
                    PipeName = Contracts.PipeName,
                    ServerName = ".",
                    AccessControl = ps =>
                    {
                    },
                    MaxReceivedMessageSizeInMegabytes = 100,
                    RequestTimeout = TimeSpan.FromHours(10)
                },
                new BidirectionalHttp.ListenerConfig()
                {
                    Uri = serverUri,
                    RequestTimeout = TimeSpan.FromHours(1)
                }
            ]
        };

        try
        {
            await ipcServer.Start(); // ar putea fi void, ar putea fi si Run
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to start.");
            Console.WriteLine(ex.ToString());
            throw;
        }

        var key1 = new NamedPipeConnectionKey(Contracts.PipeName, ".");

        IpcClient.Config(key1, new()
        {
            ServiceProvider = clientSP,
            Callbacks = new()
            {
                typeof(Contracts.IClientOperations),
                { typeof(Contracts.IClientOperations2), new Impl.Client2() }
            },
            Scheduler = clientScheduler
        });

        var key3 = new BidirectionalHttp.ConnectionKey()
        {
            ServerUri = serverUri,
            ClientUri = clientUri
        };
        IpcClient.Config(key3, new()
        {
            ServiceProvider = clientSP,
            Callbacks = new()
            {
                typeof(Contracts.IClientOperations),
                { typeof(Contracts.IClientOperations2), new Impl.Client2() }
            },
            Scheduler = clientScheduler
        });

        IpcClient.Connect<IOrderedQueryable>(new NamedPipeConnectionKey(Contracts.PipeName, "."));

        var proxy3 = IpcClient.Connect<Contracts.IServerOperations>(key3);
        await proxy3.Register();
        await proxy3.Broadcast("Hello Bidirectional Http!");

        await Task.WhenAny(cancelled);
    }
}