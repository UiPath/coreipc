using AutoFixture;
using Newtonsoft.Json;
using Nito.AsyncEx;
using Nito.Disposables;
using NSubstitute;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using UiPath.Ipc.Transport.NamedPipe;
using UiPath.Ipc.Transport.Tcp;
using UiPath.Ipc.Transport.WebSocket;
using Xunit;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public abstract class ComputingTests : TestBase
{
    #region " Setup "
    protected readonly ComputingCallback _computingCallback = new();

    private readonly Lazy<ComputingService> _service;
    private readonly Lazy<IComputingService?> _proxy;

    protected ComputingService Service => _service.Value;
    protected IComputingService Proxy => _proxy.Value!;

    protected sealed override IpcProxy? IpcProxy => Proxy as IpcProxy;
    protected sealed override Type ContractType => typeof(IComputingService);

    protected readonly ConcurrentBag<CallInfo> _clientBeforeCalls = new();

    protected ComputingTests(ITestOutputHelper outputHelper) : base(outputHelper)
    {
        ServiceProvider.InjectLazy(out _service);
        CreateLazyProxy(out _proxy);
    }

    protected override void ConfigureSpecificServices(IServiceCollection services)
    => services
        .AddSingleton<SystemService>()
        .AddSingletonAlias<ISystemService, SystemService>()
        .AddSingleton<ComputingService>()
        .AddSingletonAlias<IComputingService, ComputingService>()
        ;

    protected override ListenerConfig ConfigTransportAgnostic(ListenerConfig listener)
    => listener with
    {
        ConcurrentAccepts = 10,
        RequestTimeout = Timeouts.DefaultRequest,
        MaxReceivedMessageSizeInMegabytes = 1,
    };
    protected override ClientConfig CreateClientConfig(EndpointCollection? callbacks = null)
    => new()
    {
        RequestTimeout = Timeouts.DefaultRequest,
        Scheduler = GuiScheduler,
        Callbacks = callbacks ?? new()
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
            waitOnServer: TimeSpan.Zero,
            message: new()
            {
                RequestTimeout = Timeouts.DefaultRequest
            })
            .ShouldBeAsync(Names.GuiThreadName)
            .ShouldNotThrowAsync();
    }

    private sealed class ShortClientTimeout : OverrideConfig
    {
        public override IpcClient? Override(Func<IpcClient> client) => client().WithRequestTimeout(TimeSpan.FromMilliseconds(10));
    }

    [Theory, IpcAutoData]
    public async Task CallsWithArraysOfStructsAsParams_ShouldWork(ComplexNumber a, ComplexNumber b, ComplexNumber c)
    => await Proxy.AddComplexNumberList([a, b, c]).ShouldBeAsync(a + b + c);

    [Fact]
    public async Task Callbacks_ShouldWork()
    => await Proxy.GetCallbackThreadName(waitOnServer: TimeSpan.Zero).ShouldBeAsync(Names.GuiThreadName);

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
            ComputingService.Context = $"{callInfo.Method.Name}-{id}";
            return Task.CompletedTask;
        };

        await Proxy.GetCallContext().ShouldBeAsync(expectedCallContext);
    }

    [Fact]
    [OverrideConfig(typeof(SetBeforeConnect))]
    public async Task BeforeConnect_ShouldWork()
    {
        int callCount = 0;
        SetBeforeConnect.Set(async _ => callCount++);

        await Proxy.AddFloats(1, 2).ShouldBeAsync(3);
        callCount.ShouldBe(1);

        await Proxy.AddFloats(1, 2).ShouldBeAsync(3);
        callCount.ShouldBe(1);

        await IpcProxy.CloseConnection();
        await Proxy.AddFloats(1, 2).ShouldBeAsync(3);
        callCount.ShouldBe(2);
    }

    private sealed class SetBeforeConnect : OverrideConfig
    {
        private static readonly AsyncLocal<BeforeConnectHandler> ValueStorage = new();
        public static void Set(BeforeConnectHandler value) => ValueStorage.Value = value;

        public override IpcClient? Override(Func<IpcClient> client)
        => client().WithBeforeConnect(ct => ValueStorage.Value.ShouldNotBeNull().Invoke(ct));
    }

#if !NET461 && !CI
    [SkippableFact]
#endif
    [OverrideConfig(typeof(DisableInProcClientServer))]
    public async Task BeforeConnect_ShouldStartExternalServerJIT()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test works only on Windows.");

        using var whereDotNet = new Process
        {
            StartInfo =
            {
                FileName = "where.exe",
                Arguments = "dotnet.exe",
            }
        };
        var pathDotNet = await whereDotNet.RunReturnStdOut();

        var externalServerParams = RandomServerParams();
        var arg = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(externalServerParams)));

        var pipeName = $"{Guid.NewGuid():N}";

        using var serverProcess = new Process
        {
            StartInfo =
            {
                FileName = pathDotNet,
                Arguments = $"\"{Assembly.GetExecutingAssembly().Location}\" {arg}",
                UseShellExecute = false,
            },
        };
        using var killProcess = new Disposable(() =>
        {
            try
            {
                serverProcess.Kill();
            }
            catch
            {
            }
            _outputHelper.WriteLine("Killed server process");
        });
        var proxy = new IpcClient
        {
            Config = new()
            {
                Scheduler = GuiScheduler,
                BeforeConnect = async (_) =>
                {
                    serverProcess.Start();
                    var time = TimeSpan.FromSeconds(1);
                    _outputHelper.WriteLine($"Server started. Waiting {time}. PID={serverProcess.Id}");
                    await Task.Delay(time);
                },
            },
            Transport = externalServerParams.CreateClientTransport()
        }.GetProxy<IComputingService>();

        await proxy.AddFloats(1, 2).ShouldBeAsync(3);
    }

    [SkippableFact]
    public async Task ManyConnections_ShouldWork()
    {
        const int CParallelism = 10;
        const int CTimesEach = 100;

        await Enumerable.Range(1, CParallelism)
            .Select(async index =>
            {
                var mockCallback = Substitute.For<IComputingCallback>();
                mockCallback.AddInts(0, 1).Returns(1);

                var proxy = CreateClient(callbacks: new()
                {
                    { typeof(IComputingCallback), mockCallback }
                })!.GetProxy<IComputingService>();

                foreach (var time in Enumerable.Range(1, CTimesEach))
                {
                    await (proxy as IpcProxy)!.CloseConnection();

                    mockCallback.ClearReceivedCalls();
                    await proxy.MultiplyInts(1, 1).ShouldBeAsync(1);
                    await mockCallback.Received().AddInts(0, 1);
                }
            })
            .WhenAll();
    }

    public abstract IAsyncDisposable? RandomTransportPair(out ListenerConfig listener, out ClientTransport transport);

    public abstract ExternalServerParams RandomServerParams();
    public readonly record struct ExternalServerParams(ServerKind Kind, string? PipeName = null, int Port = 0)
    {
        public IAsyncDisposable? CreateListenerConfig(out ListenerConfig listenerConfig)
        {
            switch (Kind)
            {
                case ServerKind.NamedPipes:
                    {
                        listenerConfig = new NamedPipeListener() { PipeName = PipeName! };
                        return null;
                    }
                case ServerKind.Tcp:
                    {
                        listenerConfig = new TcpListener() { EndPoint = new(System.Net.IPAddress.Loopback, Port) };
                        return null;
                    }
                case ServerKind.WebSockets:
                    {
                        var context = new WebSocketContext(Port);
                        listenerConfig = new WebSocketListener { Accept = context.Accept };
                        return context;
                    }
                default:
                    throw new NotSupportedException($"Kind not supported. Kind was {Kind}");
            }
        }

        public ClientTransport CreateClientTransport() => Kind switch
        {
            ServerKind.NamedPipes => new NamedPipeTransport() { PipeName = PipeName! },
            ServerKind.Tcp => new TcpTransport() { EndPoint = new(System.Net.IPAddress.Loopback, Port) },
            ServerKind.WebSockets => new WebSocketTransport() { Uri = new($"ws://localhost:{Port}") },
            _ => throw new NotSupportedException($"Kind not supported. Kind was {Kind}")
        };
    }
    public enum ServerKind { NamedPipes, Tcp, WebSockets }

    private sealed class DisableInProcClientServer : OverrideConfig
    {
        public override async Task<ListenerConfig?> Override(Func<Task<ListenerConfig>> listener) => null;
        public override IpcClient? Override(Func<IpcClient> client) => null;
    }
}
