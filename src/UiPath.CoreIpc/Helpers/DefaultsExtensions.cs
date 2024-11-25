using Microsoft.Extensions.Logging.Abstractions;

namespace UiPath.Ipc;

internal static class DefaultsExtensions
{
    public static ILogger? MaybeCreateLogger(this IServiceProvider? serviceProvider, string category) => serviceProvider?.GetService<ILoggerFactory>()?.CreateLogger(category);

    public static ILogger OrDefault(this ILogger? logger) => logger ?? NullLogger.Instance;
    public static BeforeCallHandler OrDefault(this BeforeCallHandler? beforeCallHandler) => beforeCallHandler ?? DefaultBeforeCallHandler;
    public static TaskScheduler OrDefault(this TaskScheduler? scheduler) => scheduler ?? TaskScheduler.Default;
    public static ContractToSettingsMap OrDefault(this ContractToSettingsMap? map) => map ?? EmptyContractToSettingsMap;
    public static EndpointCollection OrDefault(this EndpointCollection? endpoints) => endpoints ?? new();

    public static Func<T>? MaybeCreateServiceFactory<T>(this IServiceProvider? serviceProvider) where T : class
    {
        if (serviceProvider is null)
        {
            return null;
        }

        return serviceProvider.GetRequiredService<T>;
    }

    private static readonly BeforeCallHandler DefaultBeforeCallHandler = (_, _) => Task.CompletedTask;

    private static readonly ContractToSettingsMap EmptyContractToSettingsMap = new();
}
