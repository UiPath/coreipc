namespace UiPath.Ipc;

internal interface IServiceClientConfig
{
    TimeSpan RequestTimeout { get; }
    BeforeConnectHandler? BeforeConnect { get; }
    BeforeCallHandler? BeforeCall { get; }
    ILogger? Logger { get; }
    ISerializer? Serializer { get; }
    string DebugName { get; }
}
