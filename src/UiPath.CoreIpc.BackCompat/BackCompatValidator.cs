using UiPath.Ipc;

namespace UiPath.Ipc.BackCompat;

public static class BackCompatValidator
{
    public static void Validate(ServiceHostBuilder serviceHostBuilder)
    {
        foreach (var endpointSettings in serviceHostBuilder.Endpoints.Values)
        {
            endpointSettings.Validate();
        }
    }

    public static void Validate<T>(ServiceClientBuilder<T> builder) where T : class
    => Validator.Validate(typeof(T), builder.CallbackContract);

}
