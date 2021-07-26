using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace UiPath.CoreIpc.Tests
{
    public abstract class SystemTests<TBuilder> : TestBase where TBuilder : ServiceClientBuilder<TBuilder, ISystemService>
    {
        protected ServiceHost _systemHost;
        protected ISystemService _systemClient;
        protected readonly SystemService _systemService;
        public SystemTests()
        {
            _systemService = (SystemService)_serviceProvider.GetService<ISystemService>();
            _systemHost = Configure(new ServiceHostBuilder(_serviceProvider))
                .AddEndpoint<ISystemService>()
                .ValidateAndBuild();
            _systemHost.RunAsync(GuiScheduler);
            _systemClient = CreateSystemService();
        }
        public override void Dispose()
        {
            ((IDisposable)_systemClient).Dispose();
            _systemHost.Dispose();
        }
        [Fact]
        public async Task ConcurrentRequests()
        {
            var infinite = _systemClient.Infinite();
            await Guid();
            infinite.IsCompleted.ShouldBeFalse();
        }
        [Fact]
        public async Task OptionalMessage()
        {
            var returnValue = await _systemClient.ImpersonateCaller();
            returnValue.ShouldBe(Environment.UserName);
        }

        [Fact]
        public async Task ServerTimeout()
        {
            var ex = _systemClient.Infinite().ShouldThrow<RemoteException>();
            ex.Message.ShouldBe($"{nameof(_systemClient.Infinite)} timed out.");
            ex.Is<TimeoutException>().ShouldBeTrue();
            await Guid();
        }
        [Fact]
        public async Task Void()
        {
            _systemService.DidNothing = false;
            await _systemClient.DoNothing();
            _systemService.DidNothing.ShouldBeFalse();
            while (!_systemService.DidNothing)
            {
                await Task.Delay(1);
            }
        }

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
        public async Task VoidIsAsync() => await _systemClient.VoidSyncThrow();

        [Fact]
        public async Task GetThreadName() => (await _systemClient.GetThreadName()).ShouldBe("GuiThread");

        [Fact]
        public async Task Echo()
        {
            using var stream = await _systemClient.Echo(new MemoryStream(Encoding.UTF8.GetBytes("Hello world")));
            (await new StreamReader(stream).ReadToEndAsync()).ShouldBe("Hello world");
        }

        [Fact]
        public async Task CancelUpload()
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("Hello world"));
            await _systemClient.GetThreadName();
            var cancellationSource = new CancellationTokenSource(10);
            _systemClient.Upload(stream, 20, cancellationSource.Token).ShouldThrow<TaskCanceledException>();
            await _systemClient.GetThreadName();
        }

        [Fact]
        public async Task Upload() => (await _systemClient.Upload(new MemoryStream(Encoding.UTF8.GetBytes("Hello world")))).ShouldBe("Hello world");

        [Fact]
        public async Task Download()
        {
            using var stream = await _systemClient.Download("Hello world");
            (await new StreamReader(stream).ReadToEndAsync()).ShouldBe("Hello world");
        }
        protected abstract TBuilder SystemClientBuilder();
        [Fact]
        public async Task BeforeCall()
        {
            bool newConnection = false;
            var proxy = SystemClientBuilder().BeforeCall(async (c, _) =>
            {
                newConnection = c.NewConnection;
                c.MethodName.ShouldBe(nameof(ISystemService.DoNothing));
                c.Arguments.Single().ShouldBe(""); // cancellation token
            }).ValidateAndBuild();
            newConnection.ShouldBeFalse();

            await proxy.DoNothing();
            newConnection.ShouldBeTrue();

            await proxy.DoNothing();
            newConnection.ShouldBeFalse();

            ((IpcProxy)proxy).CloseConnection();
            newConnection.ShouldBeFalse();
            await Task.Delay(1);
            await proxy.DoNothing();
            newConnection.ShouldBeTrue();

            await proxy.DoNothing();
            newConnection.ShouldBeFalse();
        }

        [Fact]
        public async Task DontReconnect()
        {
            var proxy = SystemClientBuilder().DontReconnect().ValidateAndBuild();
            await proxy.GetGuid(System.Guid.Empty);
            ((IpcProxy)proxy).CloseConnection();
            proxy.GetGuid(System.Guid.Empty).ShouldThrow<ObjectDisposedException>();
        }
        [Fact]
        public Task CancelServerCall() => CancelServerCallCore(10);
        protected ISystemService CreateSystemService() => SystemClientBuilder().ValidateAndBuild();

        async Task CancelServerCallCore(int counter)
        {
            for (int i = 0; i < counter; i++)
            {
                var proxy = CreateSystemService();
                var request = new SystemMessage { RequestTimeout = Timeout.InfiniteTimeSpan, Delay = Timeout.Infinite, Text = System.Guid.NewGuid().ToString() };
                var sendMessageResult = proxy.MissingCallback(request);
                var newGuid = System.Guid.NewGuid();
                (await proxy.GetGuid(newGuid)).ShouldBe(newGuid);
                await Task.Delay(1);
                ((IpcProxy)proxy).CloseConnection();
                sendMessageResult.ShouldThrow<Exception>();
                while (_systemService.MessageText != request.Text)
                {
                    await Task.Yield();
                }
                newGuid = System.Guid.NewGuid();
                (await proxy.GetGuid(newGuid)).ShouldBe(newGuid);
            }
        }
        [Fact]
        public virtual async void BeforeCallServerSide()
        {
            var newGuid = System.Guid.NewGuid();
            var methodName = "";
            using var protectedService = Configure(new ServiceHostBuilder(_serviceProvider))
                .AddEndpoint(new EndpointSettings<ISystemService>
                {
                    BeforeCall = async (call, ct) =>
                    {
                        methodName = call.MethodName;
                        call.Arguments[0].ShouldBe(newGuid);
                    }
                })
                .ValidateAndBuild();
            _ = protectedService.RunAsync();
            await CreateSystemService().GetGuid(newGuid);
            methodName.ShouldBe("GetGuid");
        }
    }
}