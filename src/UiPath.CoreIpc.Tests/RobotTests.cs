﻿using NSubstitute;
using System.Collections.Concurrent;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public abstract class RobotTests : TestBase
{
    #region " Setup "
    protected readonly StudioEvents _studioEvents = new();

    private readonly Lazy<StudioOperations> _service;
    private readonly Lazy<IStudioOperations?> _proxy;

    protected StudioOperations Service => _service.Value;
    protected IStudioOperations Proxy => _proxy.Value!;

    protected sealed override IpcProxy? IpcProxy => Proxy as IpcProxy;
    protected sealed override Type ContractType => typeof(IStudioOperations);
    protected override ContractCollection? Callbacks => new()
    {
        { typeof(IStudioEvents), _studioEvents }
    };

    protected readonly ConcurrentBag<CallInfo> _clientBeforeCalls = new();

    protected RobotTests(ITestOutputHelper outputHelper) : base(outputHelper)
    {
        ServiceProvider.InjectLazy(out _service);
        CreateLazyProxy(out _proxy);
    }

    protected override void ConfigureSpecificServices(IServiceCollection services)
    => services
        .AddSingleton<StudioOperations>()
        .AddSingletonAlias<IStudioOperations, StudioOperations>();

    protected override TimeSpan ServerRequestTimeout => Timeouts.DefaultRequest;
    #endregion

    [Fact]
    public async Task StudioEvents_ShouldWork()
    {
        var spy = Substitute.For<IStudioEvents>();
        using var spyInstallation = _studioEvents.RouteTo(spy);

        await Proxy.SetOffline(true);
        await spy.ReceivedWithAnyArgs(0).OnRobotInfoChanged(Arg.Any<RobotInfoChangedArgs>());

        var info = await GetProxy<IStudioAgentOperations>()!.GetRobotInfoCore(message: new());
        await spy.ReceivedWithAnyArgs(0).OnRobotInfoChanged(Arg.Any<RobotInfoChangedArgs>());

        await Proxy.SetOffline(false);
        await spy.Received(1).OnRobotInfoChanged(Arg.Is<RobotInfoChangedArgs>(x => !x.LatestInfo.Offline));

        await Proxy.SetOffline(true);
        await spy.Received(1).OnRobotInfoChanged(Arg.Is<RobotInfoChangedArgs>(x => x.LatestInfo.Offline));
    }
}
