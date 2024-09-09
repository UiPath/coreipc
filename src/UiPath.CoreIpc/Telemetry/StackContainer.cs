namespace UiPath.Ipc;

internal static class StackContainer<T> where T : Telemetry.RecordBase
{
    public static readonly AsyncLocal<Stack<T>> Storage = new();    

    public static IDisposable Push(T record)
    {
        (Storage.Value ??= new()).Push(record);
        return new Pop(record);
    }

    private sealed class Pop : IDisposable
    {
        private readonly T _value;

        public Pop(T value) => _value = value;

        public void Dispose()
        {
            var stack = (Storage.Value ??= new());
            if (stack.Count is 0)
            {
                throw new FullStackTraceException("Expecting stack to contain at least one item.");
            }

            var actual = stack.Peek();
            if (actual != _value)
            {
                throw new FullStackTraceException($"Expecting stack's head to be a certain item. Expected={_value}. Actual={actual}.");
            }
            _ = stack.Pop();
        }
    }
}
