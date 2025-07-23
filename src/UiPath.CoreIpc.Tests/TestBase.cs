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
    protected BeforeCallHandler? _tailBeforeCall = null;

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
        _serviceProvider = IpcHelpers.ConfigureServices(_outputHelper, ConfigureSpecificServices);

        _ipcServer = new(CreateIpcServer);
        _ipcClient = new(() => CreateIpcClient());

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

    protected abstract void ConfigureSpecificServices(IServiceCollection services);

    protected virtual ContractCollection? Callbacks => [];

    private Task<IpcServer?> CreateIpcServer()
    {
        if (_overrideConfig is not null)
        {
            return _overrideConfig.Override(Core);
        }

        return Core()!;

        async Task<IpcServer> Core()
        {
            _outputHelper.WriteLine($"Creating {nameof(ServerTransport)}...");

            var serverTransport = await CreateServerTransport();
            ConfigTransportBase(serverTransport);

            var endpointSettings = new ContractSettings(ContractType)
            {
                BeforeIncomingCall = (callInfo, ct) =>
                {
                    _serverBeforeCalls.Add(callInfo);
                    return _tailBeforeCall?.Invoke(callInfo, ct) ?? Task.CompletedTask;
                }
            };

            return new()
            {
                Endpoints = new() { endpointSettings },
                Transport = serverTransport,
                ServiceProvider = _serviceProvider,
                Scheduler = GuiScheduler,
                RequestTimeout = ServerRequestTimeout
            };
        }
    }
    protected IpcClient? CreateIpcClient(ContractCollection? callbacks = null) 
    {
        if (_overrideConfig is null)
        {
            return CreateDefaultClient();
        }

        return _overrideConfig.Override(CreateDefaultClient);

        IpcClient CreateDefaultClient()
        {
            var client = new IpcClient
            {
                Callbacks = callbacks ?? Callbacks,
                Transport = CreateClientTransport()
            };
            ConfigureClient(client);
            return client;
        }
    }

    protected TContract? GetProxy<TContract>() where TContract : class
    => _ipcClient.Value?.GetProxy<TContract>();

    protected void CreateLazyProxy<TContract>(out Lazy<TContract?> lazy) where TContract : class => lazy = new(GetProxy<TContract>);

    protected abstract Task<ServerTransport> CreateServerTransport();
    protected abstract TimeSpan ServerRequestTimeout { get; }

    protected virtual void ConfigureClient(IpcClient ipcClient)
    {
        ipcClient.RequestTimeout = Timeouts.DefaultRequest;
        ipcClient.Scheduler = GuiScheduler;
    }
    protected abstract ClientTransport CreateClientTransport();

    protected virtual void ConfigTransportBase(ServerTransport serverTransport)
    {
        serverTransport.ConcurrentAccepts = 10;
        serverTransport.MaxReceivedMessageSizeInMegabytes = 1;
    }

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
