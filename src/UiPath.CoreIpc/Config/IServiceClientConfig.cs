namespace UiPath.Ipc;

// Maybe decommission
internal interface IServiceClientConfig
{
    TimeSpan RequestTimeout { get; }
    BeforeConnectHandler? BeforeConnect { get; }
    BeforeCallHandler? BeforeCall { get; }
    ILogger? Logger { get; }
    string DebugName { get; }
}
