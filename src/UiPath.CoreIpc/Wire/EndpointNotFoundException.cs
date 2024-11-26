namespace UiPath.Ipc;

public sealed class EndpointNotFoundException : ArgumentException
{
    public string ServerDebugName { get; }
    public string EndpointName { get; }

    internal EndpointNotFoundException(string paramName, string serverDebugName, string endpointName)
    : base(paramName, FormatMessage(serverDebugName, endpointName))
    {
        ServerDebugName = serverDebugName;
        EndpointName = endpointName;
    }

    internal static string FormatMessage(string serverDebugName, string endpointName) => $"Endpoint not found. Server was \"{serverDebugName}\". Endpoint was \"{endpointName}\".";
}
