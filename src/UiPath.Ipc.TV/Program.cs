using Microsoft.Extensions.DependencyInjection;
using UiPath.Ipc.TV;

await using var serviceProvider = Di.CreateServiceProvider();
await Task.Run(() =>
{
    Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
    Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

    ApplicationConfiguration.Initialize();
    Application.Run(serviceProvider.GetRequiredService<FormMain>());
});        
