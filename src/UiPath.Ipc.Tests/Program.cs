using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using UiPath.Ipc;
using UiPath.Ipc.Tests;

if (args is not [var base64])
{
    Console.Error.WriteLine($"Usage: dotnet {Path.GetFileName(Assembly.GetEntryAssembly()!.Location)} <BASE64(AssemblyQualifiedName(ComputingTests sealed subtype))>");
    return 1;
}
var externalServerParams = JsonConvert.DeserializeObject<ComputingTests.ExternalServerParams>(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
await using var asyncDisposable = externalServerParams.CreateListenerConfig(out var serverTransport);

await using var serviceProvider = new ServiceCollection()
    .AddLogging(builder => builder.AddConsole())
    .AddSingleton<IComputingService, ComputingService>()
    .BuildServiceProvider();

await using var ipcServer = new IpcServer()
{
    ServiceProvider = serviceProvider,
    Scheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler,
    Endpoints = new()
    {
        { typeof(IComputingService) },
    },
    Transport = serverTransport,
};
ipcServer.Start();
await ipcServer.WaitForStop();

return 0;