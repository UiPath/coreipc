using static UiPath.CoreIpc.CancellationTokenSourcePool;
namespace UiPath.CoreIpc;
using BoundedQueue = BoundedConcurrentQueue<PooledCancellationTokenSource>;
// https://github.com/dotnet/aspnetcore/blob/main/src/Shared/CancellationTokenSourcePool.cs
internal static class CancellationTokenSourcePool
{
    public static PooledCancellationTokenSource Rent() =>
#if !NET461
        BoundedQueue.Rent();
#else
        new();
#endif
    static bool Return(PooledCancellationTokenSource cts) => BoundedQueue.Return(cts);
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
static class BoundedConcurrentQueue<T> where T : new()
{
    private const int MaxQueueSize = 1024;
    private static readonly ConcurrentQueue<T> Cache = new();
    private static int Count;
    public static T Rent()
    {
        if (Cache.TryDequeue(out var cts))
        {
            Interlocked.Decrement(ref Count);
            return cts;
        }
        return new();
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