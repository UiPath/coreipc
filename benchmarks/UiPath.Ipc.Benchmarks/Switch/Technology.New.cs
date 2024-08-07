using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Benchmarks;

partial class Technology
{
    public sealed class New : Technology, IProxy
    {
        private const string PipeName = "BenchmarkPipeNew";
        private const int ConcurrentAccepts = 10;
        private const int MaxRequestMB = 100;

        private static readonly TimeSpan ServerTimeout = TimeSpan.FromDays(1);
        private static readonly TimeSpan ClientTimeout = TimeSpan.FromDays(1);
        private readonly TaskScheduler _scheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;

        private readonly ComputingCallback _computingCallback = new();
        private readonly ServiceProvider _serviceProvider;
        private readonly IpcServer _server;
        private readonly ClientBase _client;
        private readonly IComputingService _proxy;

        public New()
        {
            _serviceProvider = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IComputingService, ComputingService>()
                .BuildServiceProvider();

            _server = new IpcServer
            {
                Endpoints = new()
                {
                    { typeof(IComputingService) }
                },
                Listeners = [
                    new NamedPipeListener
                    {
                        PipeName = PipeName,
                        RequestTimeout = ServerTimeout,
                        ConcurrentAccepts = ConcurrentAccepts,
                        MaxReceivedMessageSizeInMegabytes = MaxRequestMB
                    }
                ],
                Scheduler = _scheduler,
                ServiceProvider = _serviceProvider
            };

            _client = new NamedPipeClient
            {
                PipeName = PipeName,
                AllowImpersonation = true,
                RequestTimeout = TimeSpan.FromSeconds(5),
                Scheduler = _scheduler,
                Callbacks = new()
                {
                    { typeof(IComputingCallback), _computingCallback }
                }
            };

            _proxy = _client.GetProxy<IComputingService>();
        }

        public override async Task Init()
        {
            _server.Start();
            _ = await GetProxy().GetCallbackThreadName();
        }

        public override async ValueTask DisposeAsync()
        {
            (_proxy as IpcProxy)!.Dispose();
            await _server.DisposeAsync();
        }

        public override IProxy GetProxy() => this;

        Task<float> IProxy.AddFloats(float x, float y) => _proxy.AddFloats(x, y);
        Task<string> IProxy.GetCallbackThreadName() => _proxy.GetCallbackThreadName(TimeSpan.Zero);


        private interface IComputingService
        {
            Task<float> AddFloats(float x, float y, CancellationToken ct = default);
            Task<string> GetCallbackThreadName(TimeSpan duration, Message message = null!, CancellationToken cancellationToken = default);
        }

        private sealed class ComputingService(ILogger<ComputingService> logger) : IComputingService
        {
            public async Task<float> AddFloats(float a, float b, CancellationToken ct = default)
            {
                logger.LogInformation($"{nameof(AddFloats)} called.");
                return a + b;
            }

            public async Task<string> GetCallbackThreadName(TimeSpan duration, Message message = null!, CancellationToken cancellationToken = default)
            {
                await Task.Delay(duration);
                return await message.GetCallback<IComputingCallback>().GetThreadName();
            }
        }
    }
}

