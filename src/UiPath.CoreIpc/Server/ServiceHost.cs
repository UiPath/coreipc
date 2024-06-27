namespace UiPath.Ipc;
public sealed class ServiceHost : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IDictionary<string, EndpointSettings> _endpoints;
    private readonly IReadOnlyCollection<Listener> _listeners;
    internal ServiceHost(IEnumerable<Listener> listeners, IDictionary<string, EndpointSettings> endpoints)
    {
        _endpoints = endpoints.ToReadOnlyDictionary();
        _listeners = listeners.ToArray();
    }
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
    public void Run() => RunAsync().Wait();
    public Task RunAsync(TaskScheduler taskScheduler = null)
    {
        foreach (var endpoint in _endpoints.Values)
        {
            endpoint.Scheduler = taskScheduler;
        }
        return Task.Run(() => Task.WhenAll(_listeners.Select(listener => listener.Listen(_cancellationTokenSource.Token))));
    }
}