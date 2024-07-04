using Microsoft.Extensions.DependencyInjection;
using UiPath.Ipc;
using UiPath.Ipc.NamedPipe;

namespace Playground;

internal static class Setup
{
	public static async Task RunServer()
	{
		await using var sp = new ServiceCollection()
			.AddSingleton<Contracts.IServerOperations, Impl.Server>()
			.AddIpc()
			.AddLogging()
			.BuildServiceProvider();

		var host = new ServiceHostBuilder(sp)
			.UseNamedPipes(settings: new NamedPipeSettings(Contracts.PipeName))
			.AddEndpoint<Contracts.IServerOperations>()
            .AllowCallback(typeof(Contracts.IClientOperations))
			.Build();

		await host.RunAsync();
	}

    public static Contracts.IServerOperations Connect()
    => new NamedPipeClientBuilder<Contracts.IServerOperations>(Contracts.PipeName)
        .Build();
}
