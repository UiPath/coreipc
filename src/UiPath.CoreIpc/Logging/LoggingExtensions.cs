namespace UiPath.Ipc;

using static LoggingExtensions.Event;

internal static partial class LoggingExtensions
{
    private const string ServiceClient = "ServiceClient";
    private const string Connection = "Connection";

    private const int Jump = 1000;
    private enum EventCategory
    {
        ServiceClient = Jump * 0,
        Connection = Jump * 1
    }
    
    public enum Event
    {
        ServiceClient = EventCategory.ServiceClient,
        ServiceClient_Calling = ServiceClient + 1,
        ServiceClient_CalledSuccessfully = ServiceClient + 2,
        ServiceClient_FailedToCall = ServiceClient + 3,
        ServiceClient_Dispose = ServiceClient + 4,

        Connection = EventCategory.Connection,
        Connection_ReceiveLoopFailed = Connection + 1,
        Connection_ReceiveLoopEndedSuccessfully = Connection + 2,

    }

    [LoggerMessage(
        EventId = (int)Event.ServiceClient_Calling, 
        EventName = nameof(Event.ServiceClient_Calling), 
        Level = LogLevel.Debug, 
        Message = $$"""{{ServiceClient}} calling {methodName} {requestId} {debugName}.""")]
    public static partial void ServiceClient_Calling(this ILogger logger, string methodName, string requestId, string debugName);

    [LoggerMessage(
        EventId = (int)Event.ServiceClient_CalledSuccessfully,
        EventName = nameof(Event.ServiceClient_CalledSuccessfully),
        Level = LogLevel.Debug,
        Message = $$"""{{ServiceClient}} successfully called a remote method. MethodName={methodName}, RequestId={requestId}, DebugName={debugName}.""")]
    public static partial void ServiceClient_CalledSuccessfully(this ILogger logger, string methodName, string requestId, string debugName);

    [LoggerMessage(
        EventId = (int)Event.ServiceClient_FailedToCall,
        EventName = nameof(Event.ServiceClient_FailedToCall),
        Level = LogLevel.Debug,
        Message = $$"""{{ServiceClient}} failed to call a remote method. MethodName={methodName}, RequestId={requestId}, DebugName={debugName}.""")]
    public static partial void ServiceClient_FailedToCall(this ILogger logger, string methodName, string requestId, string debugName, Exception ex);

    [LoggerMessage(
        EventId = (int)Event.ServiceClient_Dispose,
        EventName = nameof(Event.ServiceClient_Dispose),
        Level = LogLevel.Debug,
        Message = $$"""{{ServiceClient}} disposed. DebugName={debugName}.""")]
    public static partial void ServiceClient_Dispose(this ILogger logger, string debugName);

    [LoggerMessage(
        EventId = (int)Event.Connection_ReceiveLoopFailed,
        EventName = nameof(Event.Connection_ReceiveLoopFailed),
        Level = LogLevel.Error,
        Message = $$"""{{Connection}} receive loop failed. DebugName={debugName}.""")]
    public static partial void Connection_ReceiveLoopFailed(this ILogger logger, string debugName, Exception ex);

    [LoggerMessage(
        EventId = (int)Event.Connection_ReceiveLoopEndedSuccessfully,
        EventName = nameof(Event.Connection_ReceiveLoopEndedSuccessfully),
        Level = LogLevel.Debug,
        Message = $$"""{{Connection}} receive loop ended successfully. DebugName={debugName}.""")]
    public static partial void Connection_ReceiveLoopEndedSuccessfully(this ILogger logger, string debugName);
}
