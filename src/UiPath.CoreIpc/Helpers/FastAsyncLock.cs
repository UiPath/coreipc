namespace UiPath.Ipc;

internal sealed class FastAsyncLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(initialCount: 1, maxCount: 1);

    public async Task<IDisposable> Lock(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return this;
    }

    public void Dispose() => _semaphore.Release();
}
