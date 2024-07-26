using UiPath.Ipc;

namespace UiPath.Ipc.BackCompat;

internal static class EndpointSettingsExtensions
{
    public static EndpointCollection ToEndpointCollection(this EndpointSettings? endpointSettings)
    {
        if (endpointSettings is null)
        {
            return [];
        }

        return new()
        {
            { endpointSettings.Service.Type, endpointSettings.Service.MaybeGetInstance() }
        };
    }
}
