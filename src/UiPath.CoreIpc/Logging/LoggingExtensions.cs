namespace UiPath.Ipc;

using static LoggingExtensions.Event;

internal static partial class LoggingExtensions
{
    private const string ServiceClient = "IpcClient";
    
    public enum Event
    {
        ServiceClient = 0,
        ServiceClient_Calling = ServiceClient + 1,
        ServiceClient_Called = ServiceClient + 2,
        ServiceClient_Dispose = ServiceClient + 3,
    }

    [LoggerMessage(
        EventId = (int)ServiceClient_Calling, 
        EventName = nameof(ServiceClient_Calling), 
        Level = LogLevel.Information, 
        Message = $$"""{{ServiceClient}} calling {methodName} {requestId} {debugName}.""")]
    public static partial void ServiceClientCalling(this ILogger logger, string methodName, string requestId, string debugName);

    [LoggerMessage(
        EventId = (int)ServiceClient_Called,
        EventName = nameof(ServiceClient_Called),
        Level = LogLevel.Information,
        Message = $$"""{{ServiceClient}} called {methodName} {requestId} {debugName}.""")]
    public static partial void ServiceClientCalled(this ILogger logger, string methodName, string requestId, string debugName);

    [LoggerMessage(
        EventId = (int)ServiceClient_Dispose,
        EventName = nameof(ServiceClient_Dispose),
        Level = LogLevel.Information,
        Message = $$"""{{ServiceClient}} dispose {debugName}.""")]
    public static partial void ServiceClientDispose(this ILogger logger, string debugName);
}
