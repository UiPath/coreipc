namespace UiPath.Rpc;
public sealed class ServiceHost : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Listener[] _listeners;
    internal ServiceHost(IEnumerable<Listener> listeners) => _listeners = listeners.ToArray();
    public void Dispose()
    {
        if(_cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }
        foreach (var listener in _listeners)
        {
            listener.Dispose();
        }
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.AssertDisposed();
    }
    public Task RunAsync() => Task.WhenAll(Array.ConvertAll(_listeners, listener => listener.Listen(_cancellationTokenSource.Token)));
}