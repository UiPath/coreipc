namespace UiPath.Ipc;

public sealed class MethodNotFoundException : ArgumentException
{
    public string ServerDebugName { get; }
    public string EndpointName { get; }
    public string MethodName { get; }

    internal MethodNotFoundException(string paramName, string serverDebugName, string endpointName, string methodName)
    : base(FormatMessage(serverDebugName, endpointName, methodName), paramName)
    {
        ServerDebugName = serverDebugName;
        EndpointName = endpointName;
        MethodName = methodName;
    }

    internal static string FormatMessage(string serverDebugName, string endpointName, string methodName) => $"Method not found. Server was \"{serverDebugName}\". Endpoint was \"{endpointName}\". Method was \"{methodName}\".";
}
