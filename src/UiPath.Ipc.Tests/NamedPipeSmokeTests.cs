using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

public sealed class NamedPipeSmokeTests
{
    [Fact]
    public async Task NamedPipesShoulNotLeak()
    {
        var pipeName = $"ipctest_{Guid.NewGuid():N}";

        (await ListPipes(pipeName)).ShouldBeNullOrEmpty();

        await using (var ipcServer = CreateServer(pipeName))
        {
            var ipcClient = CreateClient(pipeName);
            var proxy = ipcClient.GetProxy<IComputingService>();

            await ipcServer.WaitForStart();
            await proxy.AddFloats(2, 3).ShouldBeAsync(5);
        }

        (await ListPipes(pipeName)).ShouldBeNullOrEmpty();
    }

    private static IpcServer CreateServer(string pipeName)
    => new IpcServer
    {
        Transport = [
            new NamedPipeServerTransport
            {
                PipeName = pipeName,
            }
        ],
        Endpoints = new()
        {
            typeof(IComputingService)
        },
        ServiceProvider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IComputingService, ComputingService>()
            .BuildServiceProvider()
    };

    private static IpcClient CreateClient(string pipeName)
    => new()
    {
        Transport = new NamedPipeClientTransport { PipeName = pipeName },
        Config = new()
    };

    private static Task<string> ListPipes(string pattern)
    => RunPowershell($"(Get-ChildItem \\\\.\\pipe\\ | Select-Object FullName) | Where-Object {{ $_.FullName -like '{pattern}' }}");

    private static async Task<string> RunPowershell(string command)
    {
        var process = new Process
        {
            StartInfo = new()
            {
                FileName = "powershell",
                Arguments = $"-Command \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await Task.Run(process.WaitForExit);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to run powershell. ExitCode: {process.ExitCode}. Error: {error}");
        }
        return output;
    }
}
