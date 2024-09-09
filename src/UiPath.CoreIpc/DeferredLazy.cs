namespace UiPath.Ipc;

internal class DeferredLazy<T>
{
    private readonly ScopedReaderWriterLock _lock = new();
    private T _value = default!;
    private bool _haveValue;

    public T GetValue(Func<T> factory)
    {
        using (_lock.EnterReadLock())
        {
            if (_haveValue)
            {
                return _value;
            }
        }

        using (_lock.EnterUpgradeableRead())
        {
            if (_haveValue)
            {
                return _value;
            }

            using (_lock.EnterWriteLock())
            {
                _haveValue = true;
                return _value = factory();                
            }
        }
    }
}
