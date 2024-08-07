using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UiPath.CoreIpc;
using UiPath.CoreIpc.NamedPipe;

namespace UiPath.Ipc.Benchmarks;

partial class Technology
{
    public sealed class Old : Technology, IProxy
    {
        private const string PipeName = "BenchmarkPipeOld";
        private const int ConcurrentAccepts = 10;
        private const int MaxRequestMB = 100;

        private static readonly TimeSpan ServerTimeout = TimeSpan.FromDays(1);
        private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(1);
        private readonly TaskScheduler _scheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;
        private Task? _serverRunning;

        private readonly ComputingCallback _computingCallback = new();
        private readonly ServiceProvider _serverServiceProvider;
        private readonly ServiceProvider _clientServiceProvider;
        private readonly ServiceHost _serviceHost;
        private readonly IComputingService _proxy;

        public Old()
        {
            _serverServiceProvider = new ServiceCollection()
               .AddIpc()
               .AddLogging()
               .AddSingleton<IComputingService, ComputingService>()
               .BuildServiceProvider();

            _clientServiceProvider = new ServiceCollection()
                .AddIpc()
                .AddLogging()
                .AddSingleton<IComputingCallback>(_computingCallback)
                .BuildServiceProvider();

            _serviceHost = new ServiceHostBuilder(_serverServiceProvider)
                .AddEndpoint<IComputingService, IComputingCallback>()
                .UseNamedPipes(new NamedPipeSettings(PipeName)
                {
                    RequestTimeout = ServerTimeout,
                    ConcurrentAccepts = ConcurrentAccepts,
                    MaxReceivedMessageSizeInMegabytes = MaxRequestMB,
                })
                .Build();

            _proxy = new NamedPipeClientBuilder<IComputingService, IComputingCallback>(PipeName, _clientServiceProvider)
                .AllowImpersonation()
                .RequestTimeout(ClientTimeout)
                .TaskScheduler(_scheduler)
                .CallbackInstance(_computingCallback)
                .Build();
        }

        public override async Task Init()
        {
            _serverRunning = _serviceHost.RunAsync(_scheduler);
            _ = await GetProxy().GetCallbackThreadName();
        }

        public override async ValueTask DisposeAsync()
        {
            (_proxy as CoreIpc.IpcProxy)!.Dispose();
            _serviceHost.Dispose();
            await (_serverRunning ?? Task.CompletedTask);
        }

        public override IProxy GetProxy() => this;

        Task<float> IProxy.AddFloats(float x, float y) => _proxy.AddFloats(x, y);
        Task<string> IProxy.GetCallbackThreadName() => _proxy.GetCallbackThreadName(TimeSpan.Zero);

        private interface IComputingService
        {
            Task<float> AddFloats(float x, float y, CancellationToken ct = default);
            Task<string> GetCallbackThreadName(TimeSpan duration, CoreIpc.Message message = null!, CancellationToken cancellationToken = default);
        }

        private sealed class ComputingService(ILogger<ComputingService> logger) : IComputingService
        {
            public async Task<float> AddFloats(float a, float b, CancellationToken ct = default)
            {
                logger.LogInformation($"{nameof(AddFloats)} called.");
                return a + b;
            }

            public async Task<string> GetCallbackThreadName(TimeSpan duration, CoreIpc.Message message = null!, CancellationToken cancellationToken = default)
            {
                await Task.Delay(duration);
                return await message.GetCallback<IComputingCallback>().GetThreadName();
            }
        }
    }
}

