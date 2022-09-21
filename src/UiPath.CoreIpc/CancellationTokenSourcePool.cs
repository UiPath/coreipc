namespace UiPath.CoreIpc;
// https://github.com/dotnet/aspnetcore/blob/main/src/Shared/CancellationTokenSourcePool.cs
internal static class CancellationTokenSourcePool
{
    private const int MaxQueueSize = 1024;
    private static readonly ConcurrentQueue<PooledCancellationTokenSource> Cache = new();
    private static int Count;
    public static PooledCancellationTokenSource Rent()
    {
#if !NET461
        if (Cache.TryDequeue(out var cts))
        {
            Interlocked.Decrement(ref Count);
            return cts;
        }
#endif
        return new PooledCancellationTokenSource();
    }
    private static bool Return(PooledCancellationTokenSource cts)
    {
        if (!cts.TryReset())
        {
            return false;
        }
        if (Interlocked.Increment(ref Count) > MaxQueueSize)
        {
            Interlocked.Decrement(ref Count);
            return false;
        }
        Cache.Enqueue(cts);
        return true;
    }
    public sealed class PooledCancellationTokenSource : CancellationTokenSource
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // If we failed to return to the pool then dispose
                if (!Return(this))
                {
                    base.Dispose(disposing);
                }
            }
        }
#if NET461
        public bool TryReset() => false;
#endif
    }
}