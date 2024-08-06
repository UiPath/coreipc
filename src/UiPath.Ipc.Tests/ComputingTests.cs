using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public abstract class ComputingTests : TestBase
{
    #region " Setup "
    protected readonly ComputingCallback _computingCallback = new();

    private readonly Lazy<ComputingService> _service;
    private readonly Lazy<IComputingService> _proxy;

    protected ComputingService Service => _service.Value;
    protected IComputingService Proxy => _proxy.Value;

    protected sealed override IpcProxy IpcProxy => Proxy as IpcProxy ?? throw new InvalidOperationException($"Proxy was expected to be a {nameof(IpcProxy)} but was not.");
    protected sealed override Type ContractType => typeof(IComputingService);

    protected ComputingTests(ITestOutputHelper outputHelper) : base(outputHelper)
    {
        ServiceProvider.InjectLazy(out _service);
        CreateLazyProxy(out _proxy);
    }

    protected override ListenerConfig ConfigTransportAgnostic(ListenerConfig listener)
    => listener with
    {
        ConcurrentAccepts = 10,
        RequestTimeout = Debugger.IsAttached ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(2),
        MaxReceivedMessageSizeInMegabytes = 1,
    };
    protected override ClientBase ConfigTransportAgnostic(ClientBase client)
    => client with
    {
        RequestTimeout = Debugger.IsAttached ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(2),
        Scheduler = GuiScheduler,
        Callbacks = new()
        {
            {typeof(IComputingCallback), _computingCallback }
        }
    };
    #endregion
}
