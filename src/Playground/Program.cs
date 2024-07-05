// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playground;
using UiPath.Ipc;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

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
        ],
    }
};
try
{
    ipcServer.EnsureStarted();
}
catch (Exception ex)
{
    Console.WriteLine("Failed to start.");
    Console.WriteLine(ex.ToString());
    throw;
}

var key = new NamedPipeConnectionKey(Contracts.PipeName);

IpcClient.Config(key, new()
{
    ServiceProvider = clientSP,
    Callbacks = new()
    {
        typeof(Contracts.IClientOperations),
        new Impl.Client2() as Contracts.IClientOperations2
    },
    Scheduler = clientScheduler
});

var proxy0 = IpcClient.Connect<Contracts.IServerOperations>(key);
await proxy0.Register();
await proxy0.Broadcast("Hello World!");

await Task.WhenAny(cancelled);
