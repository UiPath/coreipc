using Nito.AsyncEx;
using System.Drawing.Drawing2D;

namespace UiPath.Ipc.Tests;

public abstract class TestBase<TContract, TService> : IAsyncLifetime
    where TContract : class
    where TService : TContract
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AsyncContext _guiThread = new AsyncContextThread().Context;
    private readonly Lazy<IpcServer> _ipcServer;
    private readonly Lazy<TContract> _proxy;

    protected TestRunId TestRunId { get; } = TestRunId.New();
    protected IServiceProvider ServiceProvider => _serviceProvider;
    protected TaskScheduler GuiScheduler => _guiThread.Scheduler;
    protected IpcServer IpcServer => _ipcServer.Value;
    protected TService Service { get; }
    protected TContract Proxy => _proxy.Value;
    private IpcProxy IpcProxy => Proxy as IpcProxy ?? throw new InvalidOperationException($"Proxy was expected to be a {nameof(IpcProxy)} but was not.");

    public TestBase()
    {
        _guiThread.SynchronizationContext.Send(() => Thread.CurrentThread.Name = Names.GuiThreadName);
        _serviceProvider = IpcHelpers.ConfigureServices();
        _ipcServer = new(() => new()
        {
            Endpoints = new() { typeof(TContract) },
            Listeners = [CreateListener()],
            ServiceProvider = _serviceProvider,
            Scheduler = GuiScheduler
        });
        Service = _serviceProvider.GetRequiredService<TService>();
        _proxy = new(() => CreateClient().GetProxy<TContract>());
    }

    protected abstract ListenerConfig CreateListener();
    protected abstract ClientBase CreateClient();
    
    protected virtual async Task DisposeAsync()
    {
        IpcProxy.Dispose();
        await IpcProxy.CloseConnection();        
        await IpcServer.DisposeAsync();
        _guiThread.Dispose();
        await _serviceProvider.DisposeAsync();
    }

    async Task IAsyncLifetime.InitializeAsync()
    {        
        IpcServer.Start();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync();
}
