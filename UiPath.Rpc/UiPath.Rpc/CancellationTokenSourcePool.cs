namespace UiPath.Rpc;
// https://github.com/dotnet/aspnetcore/blob/main/src/Shared/CancellationTokenSourcePool.cs
static class CancellationTokenSourcePool
{
    public static PooledCancellationTokenSource Rent() =>
#if !NET461
        ObjectPool<PooledCancellationTokenSource>.TryRent() ?? new();
#else
        new();
#endif
    static bool Return(PooledCancellationTokenSource cts) => ObjectPool<PooledCancellationTokenSource>.Return(cts);
    public sealed class PooledCancellationTokenSource : CancellationTokenSource
    {
        public void Return()
        {
            // If we failed to return to the pool then dispose
    #if !NET461
            if (!TryReset() || !CancellationTokenSourcePool.Return(this))
    #endif
            {
                Dispose();
            }
        }
    }
}
static class ObjectPool<T>
{
    private const int MaxQueueSize = 1024;
    private static readonly ConcurrentQueue<T> Cache = new();
    private static int Count;
    public static T TryRent()
    {
        if (Cache.TryDequeue(out var pooled))
        {
            Interlocked.Decrement(ref Count);
            return pooled;
        }
        return pooled;
    }
    public static bool Return(T item)
    {
        if (Interlocked.Increment(ref Count) > MaxQueueSize)
        {
            Interlocked.Decrement(ref Count);
            return false;
        }
        Cache.Enqueue(item);
        return true;
    }
}