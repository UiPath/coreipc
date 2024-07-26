using Microsoft.Extensions.DependencyInjection;

namespace UiPath.Ipc.BackCompat;

public static class DiExtensions
{
    [Obsolete]
    public static IServiceCollection AddIpc(this IServiceCollection services) => services;
}
