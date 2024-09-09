namespace UiPath.Ipc.TV;

internal static class ProgressExtensions
{
    public static IProgress<T> ScheduleOn<T>(this IProgress<T> progress, TaskScheduler scheduler)
    => new ScheduledProgress<T>(target: progress, scheduler);

    public static IProgress<TFrom> Select<TFrom, TTo>(this IProgress<TTo> progress, Func<TFrom, TTo> selector)
    => new AdapterProgress<TFrom, TTo>(progress, selector);

    private sealed class ScheduledProgress<T> : IProgress<T>
    {
        private readonly IProgress<T> _target;
        private readonly TaskScheduler _scheduler;

        public ScheduledProgress(IProgress<T> target, TaskScheduler scheduler)
        {
            _target = target;
            _scheduler = scheduler;
        }

        public void Report(T value)
        {
            _scheduler.Run(() => _target.Report(value));
        }
    }

    private class AdapterProgress<TFrom, TTo> : IProgress<TFrom>
    {
        private readonly IProgress<TTo> _progress;
        private readonly Func<TFrom, TTo> _selector;

        public AdapterProgress(IProgress<TTo> progress, Func<TFrom, TTo> selector)
        {
            _progress = progress;
            _selector = selector;
        }

        public void Report(TFrom value)
        => _progress.Report(_selector(value));
    }
}
