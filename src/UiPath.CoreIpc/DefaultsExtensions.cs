using Microsoft.Extensions.Logging.Abstractions;

namespace UiPath.Ipc;

internal static class DefaultsExtensions
{
    public static ISerializer OrDefault(this ISerializer? serializer) => serializer ?? IpcJsonSerializer.Instance;
    public static ILoggerFactory OrDefault(this ILoggerFactory? loggerFactory) => loggerFactory ?? NullLoggerFactory.Instance;
    public static ILogger OrDefault(this ILogger? logger) => logger ?? NullLogger.Instance;
    public static BeforeCallHandler OrDefault(this BeforeCallHandler? beforeCallHandler) => beforeCallHandler ?? DefaultBeforeCallHandler;
    public static TaskScheduler OrDefault(this TaskScheduler? scheduler) => scheduler ?? TaskScheduler.Default;

    public static Func<T>? MaybeGetFactory<T>(this IServiceProvider? serviceProvider) where T : class
    {
        if (serviceProvider is null)
        {
            return null;
        }

        return serviceProvider.GetRequiredService<T>;
    }

    private static BeforeCallHandler DefaultBeforeCallHandler = (_, _) => Task.CompletedTask;
}
