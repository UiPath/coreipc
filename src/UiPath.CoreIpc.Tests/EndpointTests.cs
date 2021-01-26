using System;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using UiPath.CoreIpc.NamedPipe;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Shouldly;

namespace UiPath.CoreIpc.Tests
{
    public class EndpointTests : IDisposable
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
        private readonly ServiceHost _host;
        private readonly IComputingService _computingClient;
        private readonly ISystemService _systemClient;
        private readonly ComputingService _computingService;
        private readonly SystemService _systemService;
        private readonly ComputingCallback _computingCallback;
        private readonly SystemCallback _systemCallback;
        private readonly IServiceProvider _serviceProvider;
        public EndpointTests()
        {
            _computingCallback = new ComputingCallback { Id = Guid.NewGuid().ToString() };
            _systemCallback = new SystemCallback { Id = Guid.NewGuid().ToString() };
            _serviceProvider = IpcHelpers.ConfigureServices();
            _computingService = (ComputingService)_serviceProvider.GetService<IComputingService>();
            _systemService = (SystemService)_serviceProvider.GetService<ISystemService>();
            _host = new ServiceHostBuilder(_serviceProvider)
                .UseNamedPipes(new NamedPipeSettings("EndpointTests") { RequestTimeout = RequestTimeout })
                .AddEndpoint<IComputingServiceBase>()
                .AddEndpoint<IComputingService, IComputingCallback>()
                .AddEndpoint<ISystemService, ISystemCallback>()
                .ValidateAndBuild();
            _host.RunAsync();
            _computingClient = ComputingClientBuilder().ValidateAndBuild();
            _systemClient = CreateSystemService();
        }
        private NamedPipeClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null) =>
            new NamedPipeClientBuilder<IComputingService, IComputingCallback>("EndpointTests", _serviceProvider)
                .AllowImpersonation()
                .RequestTimeout(RequestTimeout)
                .CallbackInstance(_computingCallback)
                .TaskScheduler(taskScheduler);
        private ISystemService CreateSystemService() => SystemClientBuilder().ValidateAndBuild();
        private NamedPipeClientBuilder<ISystemService, ISystemCallback> SystemClientBuilder() =>
            new NamedPipeClientBuilder<ISystemService, ISystemCallback>("EndpointTests", _serviceProvider).CallbackInstance(_systemCallback).RequestTimeout(RequestTimeout).AllowImpersonation();
        public void Dispose()
        {
            ((IDisposable)_computingClient).Dispose();
            ((IDisposable)_systemClient).Dispose();
            _host.Dispose();
            ((IpcProxy)_computingClient).CloseConnection();
            ((IpcProxy)_systemClient).CloseConnection();
        }
        [Fact]
        public Task CallbackConcurrently() => Task.WhenAll(Enumerable.Range(1, 50).Select(_ => CallbackCore()));
        [Fact]
        public async Task Callback()
        {
            for (int index = 0; index < 50; index++)
            {
                await CallbackCore();
                ((IpcProxy)_computingClient).CloseConnection();
            }
        }

        private async Task CallbackCore()
        {
            var proxy = new NamedPipeClientBuilder<IComputingServiceBase>("EndpointTests").RequestTimeout(RequestTimeout).AllowImpersonation().ValidateAndBuild();
            var message = new SystemMessage { Text = Guid.NewGuid().ToString() };
            var computingTask = _computingClient.SendMessage(message);
            var systemTask = _systemClient.SendMessage(message);
            var computingBaseTask = proxy.AddFloat(1, 2);
            await Task.WhenAll(computingTask, systemTask, computingBaseTask);
            systemTask.Result.ShouldBe($"{Environment.UserName}_{_systemCallback.Id}_{message.Text}");
            computingTask.Result.ShouldBe($"{Environment.UserName}_{_computingCallback.Id}_{message.Text}");
            computingBaseTask.Result.ShouldBe(3);
        }

        [Fact]
        public async Task MissingCallback()
        {
            var ex = _systemClient.MissingCallback(new SystemMessage()).ShouldThrow<RemoteException>();
            ex.Message.ShouldBe("Callback contract mismatch. Requested System.IDisposable, but it's UiPath.CoreIpc.Tests.ISystemCallback.");
            ex.Is<ArgumentException>().ShouldBeTrue();
        }
        [Fact]
        public Task CancelServerCall() => CancelServerCallCore(10);

        async Task CancelServerCallCore(int counter)
        {
            for (int i = 0; i < counter; i++)
            {
                var proxy = CreateSystemService();
                var request = new SystemMessage { RequestTimeout = Timeout.InfiniteTimeSpan, Delay = Timeout.Infinite, Text = Guid.NewGuid().ToString() };
                Task sendMessageResult;
                using (var cancellationSource = new CancellationTokenSource())
                {
                    sendMessageResult = proxy.MissingCallback(request, cancellationSource.Token);
                    var newGuid = Guid.NewGuid();
                    (await proxy.GetGuid(newGuid)).ShouldBe(newGuid);
                    await Task.Delay(1);
                    cancellationSource.Cancel();
                    sendMessageResult.ShouldThrow<TaskCanceledException>();
                    while (_systemService.MessageText != request.Text)
                    {
                        await Task.Yield();
                    }
                    newGuid = Guid.NewGuid();
                    (await proxy.GetGuid(newGuid)).ShouldBe(newGuid);
                }
                ((IDisposable)proxy).Dispose();
            }
        }

        [Fact]
        public async Task DuplicateCallbackProxies()
        {
            await _systemClient.GetThreadName();
            var proxy = CreateSystemService();
            var message = proxy.GetThreadName().ShouldThrow<InvalidOperationException>().Message;
            message.ShouldStartWith("Duplicate callback proxy instance EndpointTests");
            message.ShouldEndWith("<ISystemService, ISystemCallback>. Consider using a singleton callback proxy.");
        }
    }
}