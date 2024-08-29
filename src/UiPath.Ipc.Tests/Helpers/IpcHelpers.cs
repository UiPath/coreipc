using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

using SP = ServiceProviderServiceExtensions;

internal static class IpcHelpers
{
    public static ServiceProvider ConfigureServices(ITestOutputHelper outputHelper)
    => new ServiceCollection()
        .AddLogging(builder => builder
            .AddTraceSource(new SourceSwitch("", "All"))
            .AddXUnit(outputHelper))

        .AddSingleton<SystemService>()
        .AddSingletonAlias<ISystemService, SystemService>()

        .AddSingleton<ComputingService>()
        .AddSingletonAlias<IComputingService, ComputingService>()

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

internal static class IpcClientExtensions
{
    public static IpcClient WithRequestTimeout(this IpcClient ipcClient, TimeSpan requestTimeout)
    => new()
    {
        Config = ipcClient.Config with { RequestTimeout = requestTimeout },
        Transport = ipcClient.Transport,
    };

    public static IpcClient WithCallbacks(this IpcClient ipcClient, EndpointCollection callbacks)
    => new()
    {
        Config = ipcClient.Config with { Callbacks = callbacks },
        Transport = ipcClient.Transport,
    };

    public static IpcClient WithBeforeConnect(this IpcClient ipcClient, BeforeConnectHandler beforeConnect)
    => new()
    {
        Config = ipcClient.Config with { BeforeConnect = beforeConnect },
        Transport = ipcClient.Transport,
    };
}