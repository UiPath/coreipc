namespace UiPath.Ipc;

public class IpcProxy : DispatchProxy, IDisposable
{
    internal ServiceClient ServiceClient { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    => ServiceClient.Invoke(targetMethod!, args!);

    public void Dispose() => ServiceClient?.Dispose();

    public ValueTask CloseConnection() => ServiceClient.CloseConnection();

    public event EventHandler ConnectionClosed
    {
        add => ServiceClient.ConnectionClosed += value;
        remove => ServiceClient.ConnectionClosed -= value;
    }

    public Stream? Network => ServiceClient.Network;
}
