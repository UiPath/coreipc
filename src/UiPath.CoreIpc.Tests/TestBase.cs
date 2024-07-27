using Nito.AsyncEx;
using UiPath.Ipc.BackCompat;

namespace UiPath.Ipc.Tests;

public abstract class TestBase : IAsyncLifetime
{
    protected const int MaxReceivedMessageSizeInMegabytes = 1;
    protected static int Count = -1;
    public static readonly TimeSpan RequestTimeout =
#if CI
        TimeSpan.FromSeconds(3) +
#endif
        TimeSpan.FromSeconds(3);
        // (Debugger.IsAttached ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(2));
    protected readonly IServiceProvider _serviceProvider;
    protected readonly AsyncContext _guiThread = new AsyncContextThread().Context;

    //static TestBase()
    //{
    //    AppContext.SetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", false);
    //}
    public TestBase()
    {
        _guiThread.SynchronizationContext.Send(() => Thread.CurrentThread.Name = "GuiThread");
        _serviceProvider = IpcHelpers.ConfigureServices();
    }

    protected static int GetCount() => Interlocked.Increment(ref Count);

    protected TaskScheduler GuiScheduler => _guiThread.Scheduler;

    public virtual async Task DisposeAsync() => _guiThread.Dispose();

    protected virtual TListenerConfig Configure<TListenerConfig>(TListenerConfig listenerConfig) where TListenerConfig : ListenerConfig
    => ConfigureCore(listenerConfig, RequestTimeout, MaxReceivedMessageSizeInMegabytes);

    protected abstract ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder);

    internal static TConfig ConfigureCore<TConfig>(TConfig listenerConfig, TimeSpan requestTimeout, byte maxReceivedMessageSizeInMegabytes) where TConfig : ListenerConfig
    => listenerConfig with
    {
        RequestTimeout = requestTimeout,
        MaxReceivedMessageSizeInMegabytes = maxReceivedMessageSizeInMegabytes
    };

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync();
}