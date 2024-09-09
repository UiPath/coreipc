using Microsoft.Extensions.DependencyInjection;

namespace UiPath.Ipc.TV;

using SP = ServiceProviderServiceExtensions;

class Di
{
    public static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        return services.BuildServiceProvider();
    }
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<TaskCompletionSource<TaskScheduler>>();
        services.AddSingleton<FormMain>();
        services.AddScoped<SlowDisposable>();
        services.AddScoped<FormProject>();
        services.AddScoped<ProjectContext>();
        services.AddScoped<IProjectContext>(SP.GetRequiredService<ProjectContext>);
        services.AddScoped<FormProjectModel>();
        services.AddScoped<FormFilter>();
        services.AddScoped<FormRepo>();
    }
}
