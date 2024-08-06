namespace UiPath.Ipc.Tests;

public static class DiExtensions
{
    public static void InjectLazy<T>(this IServiceProvider serviceProvider, out Lazy<T> lazy)
    where T : class
    => lazy = new(serviceProvider.GetRequiredService<T>);
}