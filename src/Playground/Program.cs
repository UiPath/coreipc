// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playground;
using UiPath.CoreIpc.Http;
using UiPath.Ipc;

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
    Config = new()
    {
        Scheduler = serverScheduler,
        ServiceProvider = serverSP,        
        Endpoints = new()
        {
            typeof(Contracts.IServerOperations),
        },
        Callbacks = [
            typeof(Contracts.IClientOperations),
            typeof(Contracts.IClientOperations2)
        ],
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
            //new DualHttpListenerConfig()
            //{
            //    Uri = serverUri,
            //    RequestTimeout = TimeSpan.FromHours(10),
            //},
            new BidirectionalHttp.ListenerConfig()
            {
                Uri = serverUri,
                RequestTimeout = TimeSpan.FromHours(1)                
            }
        ],
    }
};
try
{
    await ipcServer.EnsureStarted();
}
catch (Exception ex)
{
    Console.WriteLine("Failed to start.");
    Console.WriteLine(ex.ToString());
    throw;
}

var key1 = new NamedPipeConnectionKey(Contracts.PipeName);
IpcClient.Config(key1, new()
{
    ServiceProvider = clientSP,
    Callbacks = new()
    {
        typeof(Contracts.IClientOperations),
        new Impl.Client2() as Contracts.IClientOperations2
    },
    Scheduler = clientScheduler
});

//var proxy0 = IpcClient.Connect<Contracts.IServerOperations>(key1);
//await proxy0.Register();

//var key2 = new DualHttpConnectionKey()
//{
//    ServerUri = serverUri,
//    ClientUri = clientUri
//};

//IpcClient.Config(key2, new()
//{
//    ServiceProvider = clientSP,
//    Callbacks = new()
//    {
//        typeof(Contracts.IClientOperations),
//        new Impl.Client2() as Contracts.IClientOperations2
//    },
//    Scheduler = clientScheduler
//});

//var proxy2 = IpcClient.Connect<Contracts.IServerOperations>(key2);

//await proxy2.Register();
//await proxy2.Broadcast("Hello Http!");

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
        new Impl.Client2() as Contracts.IClientOperations2
    },
    Scheduler = clientScheduler
});

var proxy3 = IpcClient.Connect<Contracts.IServerOperations>(key3);
await proxy3.Register();
await proxy3.Broadcast("Hello Bidirectional Http!");

await Task.WhenAny(cancelled);
