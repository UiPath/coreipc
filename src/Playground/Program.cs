// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Playground;
using UiPath.Ipc;
using UiPath.Ipc.NamedPipe;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
var serverRunning = Setup.RunServer();

// debatable ca si forma

// daca auziti ca vine un request de a invoca IClientOperations, vedeti ca ASTA e IClientOperations-u
// PROCESS-WIDE
Callback.Set<Contracts.IClientOperations>(new Impl.Client(async text =>
{
    Console.WriteLine($"Server: {text}");
    return true;
}));

Callback.Set<Contracts.IClientOperations>(
    new Impl.Client(async text =>
    {
        Console.WriteLine($"V2 Server: {text}");
        return true;
    }));

await using var serviceProvider = new ServiceCollection()
    .AddSingleton(() => DateTime.Now)
    .AddScoped<Contracts.IClientOperations2, Impl.Client2>()
    .AddLogging(builder => builder.AddConsole())
    .BuildServiceProvider();

IpcClient.Config[new NamedPipeChannel(Contracts.PipeName)] = new()
{
    ServiceProvider = serviceProvider,
    Callbacks = new()
    {
        typeof(Contracts.IClientOperations2),
        new Impl.Client(async text =>
        {
            Console.WriteLine($"V3 Server: {text}");
            return true;
        }) as Contracts.IClientOperations
    },
};

var proxy0 = IpcClient.Connect<Contracts.IServerOperations>(new NamedPipeChannel(Contracts.PipeName));
await proxy0.Register();
await proxy0.Broadcast("Hello World!");

var proxy = new NamedPipeClientBuilder<Contracts.IServerOperations>(Contracts.PipeName).Build();
await proxy.Register();

var proxy2 = new NamedPipeClientBuilder<Contracts.IServerOperations>(Contracts.PipeName).Build();
await proxy2.Register();

var proxy3 = new NamedPipeClientBuilder<Contracts.IServerOperations>(Contracts.PipeName).Build();
await proxy3.Register();

await proxy.Broadcast("Hello World!");

await Task.WhenAny(serverRunning, cancelled);
