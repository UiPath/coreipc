namespace UiPath.Ipc;

// Maybe decommission
internal interface IClientConfig
{
    TimeSpan? RequestTimeout { get; }
    BeforeConnectHandler? BeforeConnect { get; }
    BeforeCallHandler? BeforeOutgoingCall { get; }
    ILogger? Logger { get; }
    string GetComputedDebugName();
}
