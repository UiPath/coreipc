// See https://aka.ms/new-console-template for more information
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

var proxy = new NamedPipeClientBuilder<Contracts.IServerOperations>(Contracts.PipeName).Build();
await proxy.Register();

var proxy2 = new NamedPipeClientBuilder<Contracts.IServerOperations>(Contracts.PipeName).Build();
await proxy2.Register();

var proxy3 = new NamedPipeClientBuilder<Contracts.IServerOperations>(Contracts.PipeName).Build();
await proxy3.Register();

await proxy.Broadcast("Hello World!");

await Task.WhenAny(serverRunning, cancelled);
