using Microsoft.Extensions.Logging;

namespace UiPath.Ipc.Tests;

using SP = ServiceProviderServiceExtensions;

internal static class IpcHelpers
{
    public static ServiceProvider ConfigureServices()
    => new ServiceCollection()
        .AddLogging(b => b.AddTraceSource(new SourceSwitch("", "All")))

        .AddSingleton<SystemService>()
        .AddSingletonAlias<ISystemService, SystemService>()

        .AddSingleton<ComputingService>()
        .AddSingletonAlias<IComputingService, ComputingService>()
        .AddSingletonAlias<IComputingServiceBase, ComputingService>()

        .BuildServiceProvider();

    private static IServiceCollection AddSingletonAlias<TNew, TExisting>(this IServiceCollection services)
        where TNew : class
        where TExisting : class, TNew
    => services.AddSingleton<TNew>(SP.GetRequiredService<TExisting>);

    public static IServiceProvider GetRequired<T>(this IServiceProvider serviceProvider, out T service) where T : class
    {
        service = serviceProvider.GetRequiredService<T>();
        return serviceProvider;
    }
}
