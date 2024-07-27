namespace UiPath.Ipc;

[Serializable]
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

    internal static string FormatMessage(string serverDebugName, string endpointName) => $"{serverDebugName} cannot find endpoint {endpointName}";
}
