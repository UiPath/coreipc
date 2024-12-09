using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace UiPath.CoreIpc.Tests;

using SP = ServiceProviderServiceExtensions;

internal static class IpcHelpers
{
    public static ServiceProvider ConfigureServices(ITestOutputHelper outputHelper, Action<IServiceCollection>? configureSpecificServices = null)
    {
        var services = new ServiceCollection()
            .AddLogging(builder => builder
                .AddTraceSource(new SourceSwitch("", "All"))
                .AddXUnit(outputHelper));

        configureSpecificServices?.Invoke(services);

        return services
            .BuildServiceProvider();
    }

    public static IServiceCollection AddSingletonAlias<TNew, TExisting>(this IServiceCollection services)
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
    {
        ipcClient.RequestTimeout = requestTimeout;
        return ipcClient;
    }
    public static IpcServer WithRequestTimeout(this IpcServer ipcServer, TimeSpan requestTimeout)
    {
        ipcServer.RequestTimeout = requestTimeout;
        return ipcServer;
    }
    public static async Task<IpcServer> WithRequestTimeout(this Task<IpcServer> ipcServerTask, TimeSpan requestTimeout)
    => (await ipcServerTask).WithRequestTimeout(requestTimeout);

    public static IpcClient WithCallbacks(this IpcClient ipcClient, ContractCollection callbacks)
    {
        ipcClient.Callbacks = callbacks;
        return ipcClient;
    }

    public static IpcClient WithBeforeConnect(this IpcClient ipcClient, BeforeConnectHandler beforeConnect)
    {
        ipcClient.BeforeConnect = beforeConnect;
        return ipcClient;
    }
}