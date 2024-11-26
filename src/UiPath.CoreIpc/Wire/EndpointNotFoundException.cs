namespace UiPath.Ipc;

public sealed class EndpointNotFoundException : ArgumentOutOfRangeException
{
    public string ServerDebugName { get; }
    public string EndpointName { get; }

    public EndpointNotFoundException(string paramName, string serverDebugName, string endpointName)
    : base(paramName, FormatMessage(serverDebugName, endpointName))
    {
        ServerDebugName = serverDebugName;
        EndpointName = endpointName;
    }

    internal static string FormatMessage(string serverDebugName, string endpointName) => $"Endpoint not found. Server was \"{serverDebugName}\". Endpoint was \"{endpointName}\".";
}
