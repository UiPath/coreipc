namespace UiPath.Ipc;

internal readonly struct Result<T>
{
    private readonly T _value;
    private readonly Exception? _exception;

    public T Value => _exception is null ? _value : throw _exception;

    public Result(T value)
    {
        _value = value;
        _exception = null;
    }

    public Result(Exception exception)
    {
        _value = default!;
        _exception = exception;
    }
}
