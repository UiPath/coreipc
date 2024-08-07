using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Nito.AsyncEx;

[SimpleJob(RuntimeMoniker.Net461, baseline: true)]
[SimpleJob(RuntimeMoniker.Net80)]
[RPlotExporter]
public class SchedulerBenchmark
{
    private TaskScheduler _scheduler = null!;

    [Params(SchedulerKind.ConcurrentExclusive, SchedulerKind.AsyncContextThread)]
    public SchedulerKind SchedulerKind;

    [GlobalSetup]
    public void Setup()
    {
        _scheduler = SchedulerKind.Create();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scheduler = null!;
    }

    [Benchmark]
    public void Schedule()
    {
        _scheduler.RunAsync(async () =>
        {
        });
    }
}

public enum SchedulerKind
{
    ConcurrentExclusive,
    AsyncContextThread
}

public static class SchedulerKindExtensions
{
    public static TaskScheduler Create(this SchedulerKind schedulerKind)
    {
        switch (schedulerKind)
        {
            case SchedulerKind.ConcurrentExclusive:
                return new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;
            case SchedulerKind.AsyncContextThread:
                return new AsyncContextThread().Context.Scheduler;
            default:
                throw new ArgumentOutOfRangeException(nameof(schedulerKind));
        }
    }
}

public static class TaskExtensions
{
    public static Task RunAsync(this TaskScheduler scheduler, Func<Task> asyncAction)
    => Task.Factory.StartNew(asyncAction, CancellationToken.None, TaskCreationOptions.DenyChildAttach, scheduler).Unwrap();

    public static Task<T> RunAsync<T>(this TaskScheduler scheduler, Func<Task<T>> asyncFunc)
    => Task.Factory.StartNew(asyncFunc, CancellationToken.None, TaskCreationOptions.DenyChildAttach, scheduler).Unwrap();
}