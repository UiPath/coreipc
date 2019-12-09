using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Shouldly;
using UiPath.CoreIpc.NamedPipe;
using Xunit;

namespace UiPath.CoreIpc.Tests
{
    public class IpcTests : IDisposable
    {
        private const int MaxReceivedMessageSizeInMegabytes = 1;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
        private readonly ServiceHost _computingHost;
        private readonly ServiceHost _systemHost;
        private readonly IComputingService _computingClient;
        private readonly ISystemService _systemClient;
        private readonly ComputingService _computingService;
        private readonly SystemService _systemService;
        private readonly ComputingCallback _computingCallback;
        private readonly IServiceProvider _serviceProvider;
        private PipeSecurity _pipeSecurity;
        private readonly AsyncContext _guiThread = new AsyncContextThread().Context;

        public IpcTests()
        {
            _guiThread.SynchronizationContext.Send(() => Thread.CurrentThread.Name = "GuiThread");
            _computingCallback = new ComputingCallback { Id = System.Guid.NewGuid().ToString() };
            _serviceProvider = IpcHelpers.ConfigureServices();
            _computingService = (ComputingService)_serviceProvider.GetService<IComputingService>();
            _systemService = (SystemService)_serviceProvider.GetService<ISystemService>();
            _computingHost = new ServiceHostBuilder(_serviceProvider)
                .UseNamedPipes(new NamedPipeSettings("computing")
                {
                    RequestTimeout = RequestTimeout,
                    AccessControl = security => _pipeSecurity = security.LocalOnly(),
                    EncryptAndSign = true,
                })
                .AddEndpoint<IComputingService, IComputingCallback>()
                .Build();
            _systemHost = new ServiceHostBuilder(_serviceProvider)
                .UseNamedPipes(new NamedPipeSettings("system")
                {
                    RequestTimeout = RequestTimeout.Subtract(TimeSpan.FromSeconds(1)),
                    MaxReceivedMessageSizeInMegabytes = MaxReceivedMessageSizeInMegabytes,
                    ConcurrentAccepts = 10,
                })
                .AddEndpoint<ISystemService>()
                .Build();

            var taskScheduler = _guiThread.Scheduler;
            _computingHost.RunAsync(taskScheduler);
            _systemHost.RunAsync(taskScheduler);
            _computingClient = ComputingClientBuilder(taskScheduler).Build();
            _systemClient = CreateSystemService();
        }

        private NamedPipeClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null) =>
            new NamedPipeClientBuilder<IComputingService, IComputingCallback>("computing", _serviceProvider)
                .AllowImpersonation()
                .EncryptAndSign()
                .RequestTimeout(RequestTimeout)
                .CallbackInstance(_computingCallback)
                .TaskScheduler(taskScheduler);

        private ISystemService CreateSystemService() => SystemClientBuilder().Build();

        private NamedPipeClientBuilder<ISystemService> SystemClientBuilder() =>
            new NamedPipeClientBuilder<ISystemService>("system").RequestTimeout(RequestTimeout).AllowImpersonation().Logger(_serviceProvider);

#if DEBUG
        [Fact]
        public void MethodsMustReturnTask() => new Action(() => new NamedPipeClientBuilder<IInvalid>("")).ShouldThrow<ArgumentException>().Message.ShouldStartWith("Method does not return Task!");
        [Fact]
        public void DuplicateMessageParameters() => new Action(() => new NamedPipeClientBuilder<IDuplicateMessage>("")).ShouldThrow<ArgumentException>().Message.ShouldStartWith("The message must be the last parameter before the cancellation token!");
        [Fact]
        public void TheMessageMustBeTheLastBeforeTheToken() => new Action(() => new NamedPipeClientBuilder<IMessageFirst>("")).ShouldThrow<ArgumentException>().Message.ShouldStartWith("The message must be the last parameter before the cancellation token!");
        [Fact]
        public void CancellationTokenMustBeLast() => new Action(() => new NamedPipeClientBuilder<IInvalidCancellationToken>("")).ShouldThrow<ArgumentException>().Message.ShouldStartWith("The CancellationToken parameter must be the last!");
        [Fact]
        public void TheCallbackContractMustBeAnInterface() => new Action(() => new NamedPipeClientBuilder<ISystemService, IpcTests>("", _serviceProvider).Build()).ShouldThrow<ArgumentOutOfRangeException>().Message.ShouldStartWith("The contract must be an interface!");
        [Fact]
        public void TheServiceContractMustBeAnInterface() => new Action(() => new EndpointSettings<IpcTests>()).ShouldThrow<ArgumentOutOfRangeException>().Message.ShouldStartWith("The contract must be an interface!");
#endif

        [Fact]
        public void PipeExists()
        {
            IOHelpers.PipeExists("computing").ShouldBeTrue();
            IOHelpers.PipeExists("system").ShouldBeTrue();
            IOHelpers.PipeExists(System.Guid.NewGuid().ToString()).ShouldBeFalse();
        }

        [Fact]
        public async Task BeforeCall()
        {
            bool newConnection = false;
            var proxy = SystemClientBuilder().BeforeCall(async (c, _) => newConnection = c.NewConnection).Build();
            newConnection.ShouldBeFalse();

            await proxy.DoNothing();
            newConnection.ShouldBeTrue();

            await proxy.DoNothing();
            newConnection.ShouldBeFalse();

            ((InterceptorProxy)proxy).CloseConnection();
            newConnection.ShouldBeFalse();
            await Task.Delay(1);
            await proxy.DoNothing();
            newConnection.ShouldBeTrue();

            await proxy.DoNothing();
            newConnection.ShouldBeFalse();
        }

        [Fact]
        public async Task ReconnectWithEncrypt()
        {
            var proxy = ComputingClientBuilder().Build();
            for (int i = 0; i < 50; i++)
            {
                await proxy.AddFloat(1, 2);
                ((InterceptorProxy)proxy).CloseConnection();
                await proxy.AddFloat(1, 2);
            }
        }

        [Fact]
        public async Task DontReconnect()
        {
            var proxy = SystemClientBuilder().DontReconnect().Build();
            await proxy.GetGuid(System.Guid.Empty);
            ((InterceptorProxy)proxy).CloseConnection();
            proxy.GetGuid(System.Guid.Empty).ShouldThrow<ObjectDisposedException>();
        }

        [Fact]
        public async Task AddFloat()
        {
            var result = await _computingClient.AddFloat(1.23f, 4.56f);
#if NET461
            _pipeSecurity.ShouldNotBeNull();
#endif
            result.ShouldBe(5.79f);
        }

        [Fact]
        public Task CancelServerCall() => CancelServerCallCore(10);

        async Task CancelServerCallCore(int counter)
        {
            for (int i = 0; i < counter; i++)
            {
                var proxy = CreateSystemService();
                var request = new SystemMessage { RequestTimeout = Timeout.InfiniteTimeSpan, Delay = Timeout.Infinite };
                var sendMessageResult = proxy.MissingCallback(request);
                var newGuid = System.Guid.NewGuid();
                (await proxy.GetGuid(newGuid)).ShouldBe(newGuid);
                await Task.Delay(1);
                ((InterceptorProxy)proxy).CloseConnection();
                sendMessageResult.ShouldThrow<Exception>();
                newGuid = System.Guid.NewGuid();
                (await proxy.GetGuid(newGuid)).ShouldBe(newGuid);
            }
        }

        [Fact]
        public Task AddFloatConcurrently() => Task.WhenAll(Enumerable.Range(1, 100).Select(_ => AddFloat()));

        [Fact]
        public async Task AddComplexNumber()
        {
            var result = await _computingClient.AddComplexNumber(new ComplexNumber(1f, 3f), new ComplexNumber(2f, 5f));
            result.ShouldBe(new ComplexNumber(3f, 8f));
        }

        [Fact]
        public async Task ClientCancellation()
        {
            using (var cancellationSource = new CancellationTokenSource(50))
            {
                _computingClient.Infinite(cancellationSource.Token).ShouldThrow<TaskCanceledException>();
            }
            await AddFloat();
        }

        [Fact]
        public async Task OptionalMessage()
        {
            var returnValue = await _systemClient.ImpersonateCaller();
            returnValue.ShouldBe(Environment.UserName);
        }

        [Fact]
        public Task ServerName() => SystemClientBuilder().ServerName(Environment.MachineName).Build().GetGuid(System.Guid.Empty);

        [Fact]
        public async Task ServerTimeout()
        {
            var ex = _systemClient.Infinite().ShouldThrow<RemoteException>();
            ex.Message.ShouldBe($"{nameof(_systemClient.Infinite)} timed out.");
            ex.Is<TimeoutException>().ShouldBeTrue();
            await Guid();
        }

        [Fact]
        public async Task ClientTimeout()
        {
            var proxy = ComputingClientBuilder().RequestTimeout(TimeSpan.FromMilliseconds(10)).Build();
            proxy.Infinite().ShouldThrow<TimeoutException>().Message.ShouldBe($"{nameof(_computingClient.Infinite)} timed out.");
            await proxy.AddFloat(0, 0);
        }

        [Fact]
        public async Task TimeoutPerRequest()
        {
            for (int i = 0; i < 20; i++)
            {
                var request = new SystemMessage { RequestTimeout = TimeSpan.FromMilliseconds(1), Delay = 1 };
                _computingClient.SendMessage(request).ShouldThrow<TimeoutException>().Message.ShouldBe($"{nameof(_computingClient.SendMessage)} timed out.");
                await AddFloat();
            }
        }

        [Fact]
        public Task InfiniteVoid() => _computingClient.InfiniteVoid();

        [Fact]
        public async Task AddComplexNumbers()
        {
            var result = await _computingClient.AddComplexNumbers(new[]
            {
                        new ComplexNumber(0.5f, 0.4f),
                        new ComplexNumber(0.2f, 0.1f),
                        new ComplexNumber(0.3f, 0.5f),
            });
            result.ShouldBe(new ComplexNumber(1f, 1f));
        }

        [Fact]
        public async Task ConcurrentRequests()
        {
            var infinite = _systemClient.Infinite();
            await Guid();
            infinite.IsCompleted.ShouldBeFalse();
        }

        [Fact]
        public async Task Void()
        {
            _systemService.DidNothing = false;
            await _systemClient.DoNothing();
            _systemService.DidNothing.ShouldBeFalse();
            while (!_systemService.DidNothing)
            {
                await Task.Yield();
            }
        }

        [Fact]
        public Task VoidIsAsync() => _systemClient.VoidSyncThrow();

        [Fact]
        public async Task GetThreadName() => (await _systemClient.GetThreadName()).ShouldBe("GuiThread");

        [Fact]
        public async Task GetCallbackThreadName() => (await _computingClient.GetCallbackThreadName()).ShouldBe("GuiThread");

        [Fact]
        public async Task VoidThreadName()
        {
            await _systemClient.VoidThreadName();
            await _systemClient.GetThreadName();
            _systemService.ThreadName.ShouldBe("GuiThread");
        }

        [Fact]
        public async Task Enum()
        {
            var text = await _systemClient.ConvertText("hEllO woRd!", TextStyle.Upper);
            text.ShouldBe("HELLO WORD!");
        }

        [Fact]
        public async Task MaxMessageSize()
        {
            _systemClient.ReverseBytes(new byte[MaxReceivedMessageSizeInMegabytes * 1024 * 1024]).ShouldThrow<Exception>();
            await Guid();
        }


        [Fact]
        public async Task Guid()
        {
            var newGuid = System.Guid.NewGuid();
            var guid = await _systemClient.GetGuid(newGuid);
            guid.ShouldBe(newGuid);
        }

        [Fact]
        public Task LargeMessage() => _systemClient.ReverseBytes(new byte[(int)(0.7 * MaxReceivedMessageSizeInMegabytes * 1024 * 1024)]);

        [Fact]
        public async Task ReverseBytes()
        {
            var input = Encoding.UTF8.GetBytes("Test");
            var reversed = await _systemClient.ReverseBytes(input);
            reversed.ShouldBe(input.Reverse());
        }
        
        [Fact]
        public async Task MissingCallback()
        {
            var ex = _systemClient.MissingCallback(new SystemMessage()).ShouldThrow<RemoteException>();
            ex.Message.ShouldBe("Callback contract mismatch. Requested System.IDisposable, but it's not configured.");
            ex.Is<ArgumentException>().ShouldBeTrue();
            await Guid();
        }

        [Fact]
        public Task CallbackConcurrently() => Task.WhenAll(Enumerable.Range(1, 50).Select(_ => Callback()));

        [Fact]
        public async Task Callback()
        {
            var message = new SystemMessage { Text = System.Guid.NewGuid().ToString() };
            var returnValue = await _computingClient.SendMessage(message);
            returnValue.ShouldBe($"{Environment.UserName}_{_computingCallback.Id}_{message.Text}");
        }

        class JobFailedException : Exception
        {
            public JobFailedException(Error error) : base("Job has failed.", new RemoteException(error))
            {
            }
        }

        [Fact]
        public void ErrorFromRemoteException()
        {
            var innerError = new Error(new InvalidDataException("invalid"));
            var error = new Error(new JobFailedException(innerError));
            error.Type.ShouldBe(typeof(JobFailedException).FullName);
            error.Message.ShouldBe("Job has failed.");
            error.InnerError.Type.ShouldBe(typeof(InvalidDataException).FullName);
            error.InnerError.Message.ShouldBe("invalid");
        }

        public void Dispose()
        {
            ((IDisposable)_computingClient).Dispose();
            ((IDisposable)_systemClient).Dispose();
            _computingHost.Dispose();
            _systemHost.Dispose();
            _guiThread.Dispose();
            ((InterceptorProxy)_computingClient).CloseConnection();
            ((InterceptorProxy)_systemClient).CloseConnection();
        }
    }
}