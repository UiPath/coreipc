namespace UiPath.Ipc;

public abstract record EndpointConfig
{
    public TimeSpan RequestTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    internal virtual RouterConfig CreateRouterConfig(IpcServer server) => throw new NotSupportedException();

    internal virtual RouterConfig CreateCallbackRouterConfig() => throw new NotSupportedException();
}
