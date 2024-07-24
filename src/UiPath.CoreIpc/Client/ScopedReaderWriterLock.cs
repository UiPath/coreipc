namespace UiPath.Ipc;

internal sealed class ScopedReaderWriterLock : IDisposable
{
    private readonly ReaderWriterLockSlim _innerLock;
    private readonly Adapter _exitRead;
    private readonly Adapter _exitUpgradeableRead;
    private readonly Adapter _exitWrite;

    public ScopedReaderWriterLock(LockRecursionPolicy policy = LockRecursionPolicy.NoRecursion)
    {
        _innerLock = new(policy);
        _exitRead = new(_innerLock.ExitReadLock);
        _exitUpgradeableRead = new(_innerLock.ExitUpgradeableReadLock);
        _exitWrite = new(_innerLock.ExitWriteLock);
    }

    public void Dispose() => _innerLock.Dispose();

    public IDisposable EnterReadLock()
    {
        _innerLock.EnterReadLock();
        return _exitRead;
    }
    public IDisposable EnterUpgradeableRead()
    {
        _innerLock.EnterUpgradeableReadLock();
        return _exitUpgradeableRead;
    }
    public IDisposable EnterWriteLock()
    {
        _innerLock.EnterWriteLock();
        return _exitWrite;
    }

    private sealed class Adapter(Action action) : IDisposable
    {
        void IDisposable.Dispose() => action();
    }
}
