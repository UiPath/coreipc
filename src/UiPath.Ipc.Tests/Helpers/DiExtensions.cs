using Microsoft.Extensions.Hosting;

namespace UiPath.Ipc.Tests;

public static class DiExtensions
{
    public static void InjectLazy<T>(this IServiceProvider serviceProvider, out Lazy<T> lazy)
        where T : class
    => lazy = new(serviceProvider.GetRequiredService<T>);

    public static IServiceCollection AddHostedSingleton<TService, THostedImpl>(this IServiceCollection services)
        where TService : class
        where THostedImpl : class, TService, IHostedService
    => services
        .AddSingleton<TService, THostedImpl>()
        .AddHostedService(sp => (THostedImpl)sp.GetRequiredService<TService>());
}