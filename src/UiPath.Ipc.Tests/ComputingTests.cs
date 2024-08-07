﻿using Xunit.Abstractions;

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
        RequestTimeout = Debugger.IsAttached ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(4),
        MaxReceivedMessageSizeInMegabytes = 1,
    };
    protected override ClientBase ConfigTransportAgnostic(ClientBase client)
    => client with
    {
        RequestTimeout = Debugger.IsAttached ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(4),
        Scheduler = GuiScheduler,
        Callbacks = new()
        {
            {typeof(IComputingCallback), _computingCallback }
        }
    };
    #endregion

    [Theory, IpcAutoData]
    public async Task Calls_ShouldWork(float x, float y)
    => await Proxy.AddFloats(x, y).ShouldBeAsync(x + y);
    
    [Theory, IpcAutoData]
    public Task ConcurrentCalls_ShouldWork(float sameX, float sameY) => Task.WhenAll(Enumerable.Range(1, 100).Select(_ => Calls_ShouldWork(sameX, sameY)));

    [Theory, IpcAutoData]
    public async Task CallsWithStructParamsAndReturns_ShouldWork(ComplexNumber a, ComplexNumber b)
    => await Proxy.AddComplexNumbers(a, b).ShouldBeAsync(a + b);

    [Fact]
    public async Task ClientCancellations_ShouldWork()
    {
        using var cts = new CancellationTokenSource();

        var taskWaiting = Proxy.Wait(Timeout.InfiniteTimeSpan, cts.Token);

        await Task.Delay(Constants.Timeout_Short);

        taskWaiting.IsCompleted.ShouldBeFalse();

        cts.Cancel();

        await taskWaiting.ShouldCompleteInAsync(Constants.Timeout_Short).ShouldThrowAsync<OperationCanceledException>(); // in-process scheduling fast

        await Proxy.Wait(TimeSpan.Zero).ShouldCompleteInAsync(Constants.Timeout_IpcRoundtrip).ShouldBeAsync(true); // connection still alive
    }

    [Fact, OverrideConfig(typeof(ShortClientTimeout))]
    public async Task ClientTimeouts_ShouldWork()
    {
        await Proxy.Wait(Timeout.InfiniteTimeSpan).ShouldThrowAsync<TimeoutException>();

        await Proxy.GetCallbackThreadName(
            duration: TimeSpan.FromMilliseconds(500),
            message: new()
            {
                RequestTimeout = TimeSpan.FromSeconds(2)
            })
            .ShouldBeAsync(Names.GuiThreadName)
            .ShouldNotThrowAsync();
    }

    private sealed class ShortClientTimeout : OverrideConfig
    {
        public override ClientBase Override(ClientBase client)
        => client with { RequestTimeout = TimeSpan.FromMilliseconds(10) };
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
}
