using Nito.AsyncEx;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public abstract class TestBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper _outputHelper;
    private readonly IMethodInfo _xUnitMethod;
    private readonly ServiceProvider _serviceProvider;
    private readonly AsyncContext _guiThread = new AsyncContextThread().Context;
    private readonly Lazy<Task<IpcServer?>> _ipcServer;
    private readonly Lazy<IpcClient?> _ipcClient;
    private readonly OverrideConfig? _overrideConfig;

    protected TestRunId TestRunId { get; } = TestRunId.New();
    protected IServiceProvider ServiceProvider => _serviceProvider;
    protected TaskScheduler GuiScheduler => _guiThread.Scheduler;
    protected IpcServer? IpcServer { get; private set; }
    protected abstract IpcProxy? IpcProxy { get; }
    protected abstract Type ContractType { get; }

    protected readonly ConcurrentBag<CallInfo> _serverBeforeCalls = new();
    protected Func<CallInfo, CancellationToken, Task>? _tailBeforeCall = null;

    public TestBase(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;

        _xUnitMethod = CustomTestFramework.Context?.Method ?? throw new InvalidOperationException();

        string runtime =
#if NET461
    RuntimeInformation.FrameworkDescription
#else
    $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.RuntimeIdentifier}"
#endif
    ;
        _outputHelper.WriteLine($"[{runtime}] \"{_xUnitMethod.Name}\"");
        _outputHelper.WriteLine("--------------------------------------\r\n");
        _overrideConfig = GetOverrideConfig();

        _guiThread.SynchronizationContext.Send(() => Thread.CurrentThread.Name = Names.GuiThreadName);
        _serviceProvider = IpcHelpers.ConfigureServices(_outputHelper);

        _ipcServer = new(CreateServer);
        _ipcClient = new(CreateClient);

        OverrideConfig? GetOverrideConfig()
        {
            var xUnitMethod = _xUnitMethod ?? throw new InvalidOperationException();

            var overrideConfigType = xUnitMethod
                .GetCustomAttributes(typeof(OverrideConfigAttribute))
                .SingleOrDefault()?.GetConstructorArguments()
                .SingleOrDefault() as Type;

            if (overrideConfigType is null)
            {
                return null;
            }
            return Activator.CreateInstance(overrideConfigType) as OverrideConfig;
        }
    }

    private Task<ListenerConfig?> CreateListenerAndConfigure()
    {
        var factory = async () =>
        {
            _outputHelper.WriteLine("Creating listener...");
            var listener = await CreateListener();
            listener = ConfigTransportAgnostic(listener);
            return listener;
        };

        if (_overrideConfig is null)
        {
            return factory();
        }

        return _overrideConfig.Override(factory);
    }

    protected async Task<IpcServer?> CreateServer()
    {
        if (await CreateListenerAndConfigure() is not { } listener) return null;

        return new()
        {
            Endpoints = new() {
                new EndpointSettings(ContractType)
                {
                    BeforeCall = (callInfo, ct) =>
                    {
                        _serverBeforeCalls.Add(callInfo);
                        return _tailBeforeCall?.Invoke(callInfo, ct) ?? Task.CompletedTask;
                    }
                }
            },
            Listeners = [listener],
            ServiceProvider = _serviceProvider,
            Scheduler = GuiScheduler
        };
    }

    private IpcClient? CreateClient()
    {
        var factory = () =>
        {
            var config = CreateClientConfig();
            var transport = CreateClientTransport();
            var client = new IpcClient
            {
                Config = config,
                Transport = transport
            };
            return client;
        };

        if (_overrideConfig is null)
        {
            return factory();
        }

        return _overrideConfig.Override(factory);
    }
    private TContract? GetProxy<TContract>() where TContract : class
    => _ipcClient.Value?.GetProxy<TContract>();

    protected void CreateLazyProxy<TContract>(out Lazy<TContract?> lazy) where TContract : class => lazy = new(GetProxy<TContract>);

    protected abstract Task<ListenerConfig> CreateListener();

    protected abstract ClientConfig CreateClientConfig();
    protected abstract ClientTransport CreateClientTransport();

    protected abstract ListenerConfig ConfigTransportAgnostic(ListenerConfig listener);

    protected virtual async Task DisposeAsync()
    {
        IpcProxy?.Dispose();
        await (IpcProxy?.CloseConnection() ?? default);
        await (IpcServer?.DisposeAsync() ?? default);
        _guiThread.Dispose();
        await _serviceProvider.DisposeAsync();
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        IpcServer = await _ipcServer.Value;
        IpcServer?.Start();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync();
}
