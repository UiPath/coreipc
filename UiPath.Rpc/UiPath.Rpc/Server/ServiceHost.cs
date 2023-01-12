using System.Collections.ObjectModel;
namespace UiPath.Rpc;
public sealed class ServiceHost : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ReadOnlyDictionary<string, EndpointSettings> _endpoints;
    private readonly Listener[] _listeners;
    internal ServiceHost(IEnumerable<Listener> listeners, IDictionary<string, EndpointSettings> endpoints)
    {
        _endpoints = new(endpoints);
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
        _cancellationTokenSource.Dispose();
    }
    public Task RunAsync(TaskScheduler taskScheduler = null)
    {
        foreach (var endpoint in _endpoints.Values)
        {
            endpoint.Scheduler = taskScheduler;
        }
        return Task.WhenAll(Array.ConvertAll(_listeners, listener => listener.Listen(_cancellationTokenSource.Token)));
    }
}