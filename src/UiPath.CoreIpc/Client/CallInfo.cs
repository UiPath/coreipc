namespace UiPath.Ipc;

public readonly struct CallInfo
{
    public CallInfo(bool newConnection, MethodInfo method, object?[] arguments)
    {
        NewConnection = newConnection;
        Method = method;
        Arguments = arguments;
    }
    public bool NewConnection { get; }
    public MethodInfo Method { get; }
    public object?[] Arguments { get; }
    internal IReadOnlyDictionary<string, string>? HeadersCore { get; init; }
    public IReadOnlyDictionary<string, string> Headers => HeadersCore ?? HeaderCollection.Empty;
}