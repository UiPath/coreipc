using System.Collections.Concurrent;
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

    protected readonly ConcurrentBag<CallInfo> _clientBeforeCalls = new();

    protected ComputingTests(ITestOutputHelper outputHelper) : base(outputHelper)
    {
        ServiceProvider.InjectLazy(out _service);
        CreateLazyProxy(out _proxy);
    }

    protected override ListenerConfig ConfigTransportAgnostic(ListenerConfig listener)
    => listener with
    {
        ConcurrentAccepts = 10,
        RequestTimeout = Timeouts.DefaultRequest,
        MaxReceivedMessageSizeInMegabytes = 1,
    };
    protected override ClientConfig CreateClientConfig()
    => new()
    {
        RequestTimeout = Timeouts.DefaultRequest,
        Scheduler = GuiScheduler,
        Callbacks = new()
        {
            { typeof(IComputingCallback), _computingCallback }
        },
        BeforeCall = async (callInfo, _) => _clientBeforeCalls.Add(callInfo),
    };
    #endregion

    [Theory, IpcAutoData]
    public async Task Calls_ShouldWork(float x, float y)
    {
        await Proxy.AddFloats(x, y).ShouldBeAsync(x + y);
    }

    [Theory, IpcAutoData]
    public Task ConcurrentCalls_ShouldWork(float sameX, float sameY) => Task.WhenAll(Enumerable.Range(1, 50).Select(_ => Calls_ShouldWork(sameX, sameY)));

    [Theory, IpcAutoData]
    public async Task CallsWithStructParamsAndReturns_ShouldWork(ComplexNumber a, ComplexNumber b)
    => await Proxy.AddComplexNumbers(a, b).ShouldBeAsync(a + b);

    [Fact]
    public async Task ClientCancellations_ShouldWork()
    {
        using var cts = new CancellationTokenSource();

        var taskWaiting = Proxy.Wait(Timeout.InfiniteTimeSpan, cts.Token);

        await Task.Delay(Timeouts.Short);

        taskWaiting.IsCompleted.ShouldBeFalse();

        cts.Cancel();

        await taskWaiting.ShouldCompleteInAsync(Timeouts.Short).ShouldThrowAsync<OperationCanceledException>(); // in-process scheduling fast

        await Proxy.Wait(TimeSpan.Zero).ShouldCompleteInAsync(Timeouts.IpcRoundtrip).ShouldBeAsync(true); // connection still alive
    }

    [Fact, OverrideConfig(typeof(ShortClientTimeout))]
    public async Task ClientTimeouts_ShouldWork()
    {
        await Proxy.Wait(Timeout.InfiniteTimeSpan).ShouldThrowAsync<TimeoutException>();

        await Proxy.GetCallbackThreadName(
            duration: TimeSpan.Zero,
            message: new()
            {
                RequestTimeout = Timeouts.DefaultRequest
            })
            .ShouldBeAsync(Names.GuiThreadName)
            .ShouldNotThrowAsync();
    }

    private sealed class ShortClientTimeout : OverrideConfig
    {
        public override IpcClient Override(IpcClient client) => client.WithRequestTimeout(TimeSpan.FromMilliseconds(10));
    }

    [Theory, IpcAutoData]
    public async Task CallsWithArraysOfStructsAsParams_ShouldWork(ComplexNumber a, ComplexNumber b, ComplexNumber c)
    => await Proxy.AddComplexNumberList([a, b, c]).ShouldBeAsync(a + b + c);

    [Fact]
    public async Task Callbacks_ShouldWork()
    => await Proxy.GetCallbackThreadName(duration: TimeSpan.Zero).ShouldBeAsync(Names.GuiThreadName);

    [Fact]
    public async Task CallbacksWithParams_ShouldWork()
    => await Proxy.MultiplyInts(7, 1).ShouldBeAsync(7);

    [Fact]
    public async Task ConcurrentCallbacksWithParams_ShouldWork()
    => await Task.WhenAll(
        Enumerable.Range(1, 50).Select(_ => CallbacksWithParams_ShouldWork()));

    [Fact]
    public async Task BeforeCall_ShouldApplyToCallsButNotToToCallbacks()
    {
        await Proxy.GetCallbackThreadName(TimeSpan.Zero).ShouldBeAsync(Names.GuiThreadName);

        _clientBeforeCalls.ShouldContain(x => x.Method.Name == nameof(IComputingService.GetCallbackThreadName));
        _clientBeforeCalls.ShouldNotContain(x => x.Method.Name == nameof(IComputingCallback.GetThreadName));

        _serverBeforeCalls.ShouldContain(x => x.Method.Name == nameof(IComputingService.GetCallbackThreadName));
        _serverBeforeCalls.ShouldNotContain(x => x.Method.Name == nameof(IComputingCallback.GetThreadName));
    }

    [Fact]
    public async Task ServerBeforeCall_WhenSync_ShouldShareAsyncLocalContextWithTheTargetMethodCall()
    {
        await Proxy.GetCallContext().ShouldBeAsync(null);

        var id = $"{Guid.NewGuid():N}";
        var expectedCallContext = $"{nameof(IComputingService.GetCallContext)}-{id}";

        _tailBeforeCall = (callInfo, _) =>
        {
            ComputingService.CallContext = $"{callInfo.Method.Name}-{id}";
            return Task.CompletedTask;
        };

        await Proxy.GetCallContext().ShouldBeAsync(expectedCallContext);
    }
}
