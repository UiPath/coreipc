namespace UiPath.Ipc;

public abstract class IpcBase
{
    public TimeSpan RequestTimeout { get; set; } = Timeout.InfiniteTimeSpan;
    public IServiceProvider? ServiceProvider { get; set; }
    public TaskScheduler? Scheduler { get; set; }
}
