namespace UiPath.Ipc.BackCompat;

public sealed class ServiceHost : IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyList<ListenerConfig> _listeners;
    private readonly IDictionary<string, EndpointSettings> _endpoints;

    private IpcServer? _ipcServer;
    private readonly Lazy<Task> _disposing;

    internal ServiceHost(
        IServiceProvider serviceProvider,
        IEnumerable<ListenerConfig> listeners,
        IDictionary<string, EndpointSettings> endpoints)
    {
        _serviceProvider = serviceProvider;
        _listeners = listeners.ToArray();
        _endpoints = endpoints.ToReadOnlyDictionary();

        _disposing = new(DisposeCore);
    }

    public ValueTask DisposeAsync() => new(_disposing.Value);

    private async Task DisposeCore()
    {
        if (_ipcServer is null)
        {
            return;
        }

        await _ipcServer.DisposeAsync();
        await _ipcServer.WaitForStop();
    }

    public Task RunAsync(TaskScheduler? taskScheduler = null)
    {
        EndpointCollection endpointCollection = [];
        foreach (var endpoint in _endpoints.Values)
        {
            endpointCollection.Add(endpoint.Service.Type, endpoint.Service.MaybeGetInstance());
        }

        _ipcServer = new()
        {
            Endpoints = endpointCollection,
            Listeners = _listeners,
            Scheduler = taskScheduler,
            ServiceProvider = _serviceProvider,
        };

        _ipcServer.Start();
        return _ipcServer.WaitForStop();
    }
}
