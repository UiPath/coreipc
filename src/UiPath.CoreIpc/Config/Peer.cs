namespace UiPath.Ipc;

public abstract class Peer
{
    public TimeSpan RequestTimeout { get; set; } = Timeout.InfiniteTimeSpan;
    public IServiceProvider? ServiceProvider { get; set; }
    public TaskScheduler? Scheduler { get; set; }
}
