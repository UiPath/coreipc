namespace UiPath.Ipc;

public abstract class Peer
{
    public TimeSpan RequestTimeout { get; init; } = Timeout.InfiniteTimeSpan;
    public IServiceProvider? ServiceProvider { get; init; }
    public TaskScheduler? Scheduler { get; init; }

    internal virtual RouterConfig CreateRouterConfig(IpcServer server) => throw new NotSupportedException();

    internal virtual RouterConfig CreateCallbackRouterConfig() => throw new NotSupportedException();
}
