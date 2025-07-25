namespace UiPath.Ipc;

public abstract class IpcBase
{
    /// <summary>
    /// The optional default timeout for all invocation requests. Leaving it or explicitly setting it to <c>null</c> has the same effect as setting it to <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }
    public IServiceProvider? ServiceProvider { get; set; }
    public TaskScheduler? Scheduler { get; set; }
}
