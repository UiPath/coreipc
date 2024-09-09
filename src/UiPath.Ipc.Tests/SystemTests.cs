using AutoFixture;
using AutoFixture.Xunit2;
using Microsoft.Extensions.Hosting;
using System.Text;
using System.Threading.Channels;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public abstract class SystemTests : TestBase
{
    #region " Setup "
    private readonly Lazy<SystemService> _service;
    private readonly Lazy<ISystemService?> _proxy;

    protected SystemService Service => _service.Value;
    protected ISystemService Proxy => _proxy.Value!;

    protected sealed override IpcProxy IpcProxy => Proxy as IpcProxy ?? throw new InvalidOperationException($"Proxy was expected to be a {nameof(IpcProxy)} but was not.");
    protected sealed override Type ContractType => typeof(ISystemService);

    protected SystemTests(ITestOutputHelper outputHelper) : base(outputHelper)
    {
        ServiceProvider.InjectLazy(out _service);
        CreateLazyProxy(out _proxy);
    }

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
        ServiceProvider = ServiceProvider,
        Callbacks = callbacks
    };
    #endregion

    [Theory, IpcAutoData]
    public async Task PassingArgsAndReturning_ShouldWork(Guid guid)
    {
        var clone = await Proxy.EchoGuidAfter(guid, TimeSpan.Zero);
        clone.ShouldBe(guid);
    }

    [Theory, IpcAutoData]
    public async Task ConcurrentOperations_ShouldWork(Guid guid1, Guid guid2)
    {
        using var cts = new CancellationTokenSource();
        var task1 = Proxy.EchoGuidAfter(guid1, Timeout.InfiniteTimeSpan, message: null, cts.Token);

        (await Proxy.EchoGuidAfter(guid2, TimeSpan.Zero)).ShouldBe(guid2);

        task1.IsCompleted.ShouldBeFalse();
        cts.Cancel();
        var act = () => task1.ShouldCompleteInAsync(Timeouts.LocalProxyToThrowOCE);
        await act.ShouldThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NotPassingAnOptionalMessage_ShouldWork()
    => await Proxy
        .MessageReceivedAsNotNull(message: null)
        .ShouldCompleteInAsync(Timeouts.IpcRoundtrip)
        .ShouldNotThrowAsyncAnd()
        .ShouldBeAsync(true);

    [Fact]
    [OverrideConfig(typeof(ServerExecutingTooLongACall_ShouldThrowTimeout_Config))]
    public async Task ServerExecutingTooLongACall_ShouldThrowTimeout()
    => await Proxy.EchoGuidAfter(Guid.Empty, Timeout.InfiniteTimeSpan) // method takes forever but we have a server side RequestTimeout configured
        .ShouldThrowAsync<RemoteException>()
        .ShouldSatisfyAllConditionsAsync(
        [
            ex => ex.Message.ShouldBe(TimeoutHelper.ComputeTimeoutMessage(nameof(Proxy.EchoGuidAfter))),
            ex => ex.Is<TimeoutException>().ShouldBeTrue()
        ]);

    [Fact]
    [OverrideConfig(typeof(ClientWaitingForTooLongACall_ShouldThrowTimeout_Config))]
    public async Task ClientWaitingForTooLongACall_ShouldThrowTimeout()
    => await Proxy.EchoGuidAfter(Guid.Empty, Timeout.InfiniteTimeSpan) // method takes forever but we have a server side RequestTimeout configured
        .ShouldThrowAsync<TimeoutException>();

    private sealed class ServerExecutingTooLongACall_ShouldThrowTimeout_Config : OverrideConfig
    {
        public override async Task<ListenerConfig?> Override(Func<Task<ListenerConfig>> listener) => await listener() with { RequestTimeout = Timeouts.Short };
        public override IpcClient? Override(Func<IpcClient> client)
        => client().WithRequestTimeout(Timeout.InfiniteTimeSpan);
    }

    private sealed class ClientWaitingForTooLongACall_ShouldThrowTimeout_Config : OverrideConfig
    {
        public override async Task<ListenerConfig?> Override(Func<Task<ListenerConfig>> listener) => await listener() with { RequestTimeout = Timeout.InfiniteTimeSpan };
        public override IpcClient? Override(Func<IpcClient> client)
        => client().WithRequestTimeout(Timeouts.IpcRoundtrip);
    }

    private ListenerConfig ShortClientTimeout(ListenerConfig listener) => listener with { RequestTimeout = TimeSpan.FromMilliseconds(100) };
    private ListenerConfig InfiniteServerTimeout(ListenerConfig listener) => listener with { RequestTimeout = Timeout.InfiniteTimeSpan };

    [Fact]
    public async Task FireAndForget_ShouldWork()
    {
        var taskRequestHonoured = Service.ResetTripWire();

        await Proxy.FireAndForget().ShouldCompleteInAsync(Timeouts.IpcRoundtrip);
        taskRequestHonoured.IsCompleted.ShouldBeFalse();

        await taskRequestHonoured.ShouldCompleteInAsync(Timeouts.IpcRoundtrip + TimeSpan.FromMilliseconds(SystemService.MsFireAndForgetDelay));
    }

    [Fact]
    public async Task ExceedingMsgSize_ShouldBreakNetwork_ButShouldBeRecoverable()
    {
        const string Little = "a";

        const int KB = 1024;
        const int MB = 1024 * KB;
        var TooBig = new string('a', 2 * MB);

        // Prime the connection
        await Proxy.EchoString(Little).ShouldBeAsync(Little);
        var originalNetwork = (Proxy as IpcProxy)!.Network!
            .ShouldNotBeNull();

        // Send a message that is too big, the network should be closed
        await Proxy.EchoString(TooBig).ShouldThrowAsync<Exception>();

        // Send a regular message, the connection should be reestablished
        await Proxy.EchoString(Little).ShouldBeAsync(Little);

        (Proxy as IpcProxy)!.Network!
            .ShouldNotBeNull()
            .ShouldNotBeSameAs(originalNetwork);
    }

    [Fact]
    public async Task ServerCallingInexistentCallback_ShouldThrow()
    {
        var (exceptionType, exceptionMessage, marshalledExceptionType) = (await Proxy.CallUnregisteredCallback()).ShouldNotBeNull();
        exceptionType.ShouldBe(nameof(RemoteException));
        marshalledExceptionType.ShouldBe(typeof(EndpointNotFoundException).FullName);
    }

    [Fact]
    public async Task ServerCallingInexistentCallback_ShouldThrow2()
    => await Proxy.AddIncrement(1, 2).ShouldThrowAsync<RemoteException>()
        .ShouldSatisfyAllConditionsAsync([
            ex => ex.Is<EndpointNotFoundException>()
        ]);

    [Fact, OverrideConfig(typeof(RegisterCallbacks))]
    public async Task ServerCallingMultipleCallbackTypes_ShouldWork()
    => await Proxy.AddIncrement(1, 2).ShouldBeAsync(1 + 2 + 1);

    private sealed class RegisterCallbacks : OverrideConfig
    {
        public override IpcClient? Override(Func<IpcClient> client)
        => client().WithCallbacks(new()
        {
            { typeof(IComputingCallback), new ComputingCallback() },
            { typeof(IArithmeticCallback), new ArithmeticCallback() },
        });
    }

    [Fact]
    public async Task FireAndForgetOperations_ShouldNotDeliverBusinessExceptionsEvenWhenThrownSynchronously()
    => await Proxy.FireAndForgetThrowSync()
        .ShouldNotThrowAsync()
        .ShouldCompleteInAsync(Timeouts.IpcRoundtrip);

    [Fact]
    public async Task ServerScheduler_ShouldBeUsed()
    => await Proxy.GetThreadName()
        .ShouldBeAsync(Names.GuiThreadName);

    [Theory, IpcAutoData]
    public async Task UploadingStreams_ShouldWork(string str)
    {
        using var memory = new MemoryStream(Encoding.UTF8.GetBytes(str));
        await Proxy.UploadEcho(memory).ShouldBeAsync(str);
    }

    //[Theory, IpcAutoData]
    public async Task CancelingStreamUploads_ShouldThrow(string str, Guid guid)
    {
        var sourceMemory = new Memory<byte>(Encoding.UTF8.GetBytes(str));

        using var cts = new CancellationTokenSource();
        using var stream = new UploadStream();

        var taskReadCall = stream.AwaitReadCall();

        var taskUploading = Proxy.UploadEcho(stream, cts.Token);

        var readCall = await taskReadCall.ShouldCompleteInAsync(TimeSpan.FromSeconds(60));// Constants.Timeout_IpcRoundtrip);
        stream.AutoRespondByte = (byte)'a';
        var cbRead = Math.Min(readCall.Memory.Length, sourceMemory.Length);
        var sourceSlice = sourceMemory.Slice(start: 0, cbRead);
        sourceSlice.CopyTo(readCall.Memory);
        var expectedServerRead = Encoding.UTF8.GetString(sourceSlice.ToArray());

        readCall.Return(cbRead);

        taskUploading.IsCompleted.ShouldBeFalse();

        await Task.Delay(Timeouts.IpcRoundtrip); // we just replied to the read call, but canceling during stream uploads works by destroying the network
        var networkBeforeCancel = IpcProxy.Network;
        cts.Cancel();

        await taskUploading
            .ShouldThrowAsync<OperationCanceledException>()
            .ShouldCompleteInAsync(Timeouts.Short); // in-process scheduling fast

        await Proxy.EchoGuidAfter(guid, waitOnServer: TimeSpan.Zero) // we expect the connection to recover
            .ShouldBeAsync(guid);

        IpcProxy.Network.ShouldNotBeNull().ShouldNotBeSameAs(networkBeforeCancel); // and the network to be a new one
    }

    [Theory, IpcAutoData]
    public async Task UnfinishedUploads_ShouldThrowOnTheClient_AndRecover(Guid guid)
    {
        var stream = new UploadStream() { AutoRespondByte = 0 };

        await Proxy.UploadJustCountBytes(stream, serverReadByteCount: 1, TimeSpan.Zero) // the server method deliberately returns before finishing to read the entire stream
            .ShouldThrowAsync<Exception>();

        var act = async () =>
        {
            while (true)
            {
                try
                {
                    var actual = await Proxy.EchoGuidAfter(guid, TimeSpan.Zero);
                    actual.ShouldBe(guid);
                    return;
                }
                catch
                {
                }
                await Task.Delay(100);
            }
        };
        await act().ShouldCompleteInAsync(TimeSpan.FromSeconds(5));
    }

#if !CI
    [Theory, IpcAutoData]
#endif
    public async Task UnfinishedUploads_ShouldThrowOnTheClient_AndRecover_Repeat(Guid guid)
    {
        const int IterationCount = 500;
        foreach (var i in Enumerable.Range(1, IterationCount))
        {
            _outputHelper.WriteLine($"Starting iteration {i}/{IterationCount}...");
            await UnfinishedUploads_ShouldThrowOnTheClient_AndRecover(guid);
            _outputHelper.WriteLine($"Finished iteration {i}/{IterationCount}.");
        }
    }

    [Theory, IpcAutoData]
    public async Task DownloadingStreams_ShouldWork(string str)
    {
        using var stream = await Proxy.Download(str);
        using var reader = new StreamReader(stream);
        var clone = await reader.ReadToEndAsync();
        clone.ShouldBe(str);
    }

#if !CI
    [Theory, MemberData(nameof(DownloadingStreams_ShouldWork_Repeat_Cases))]
#endif
    public async Task DownloadingStreams_ShouldWork_Repeat(int index, string str)
    {
        _outputHelper.WriteLine($"Calling {nameof(DownloadingStreams_ShouldWork)}[{index}]");
        await DownloadingStreams_ShouldWork(str);
    }

    public static IEnumerable<object[]> DownloadingStreams_ShouldWork_Repeat_Cases()
    {
        var fixture = IpcAutoDataAttribute.CreateFixture();
        const int CTimes = 100;

        foreach (var time in Enumerable.Range(1, CTimes))
        {
            yield return [time, fixture.Create<string>()];
        }
    }

    [Theory, IpcAutoData]
    public async Task StreamDownloadsClosedUnfinished_ShouldNotAffectTheConnection(string str, Guid guid)
    {
        using (var stream = await Proxy.Download(str))
        {
        }

        await Proxy.EchoGuidAfter(guid, TimeSpan.Zero)
            .ShouldBeAsync(guid)
            .ShouldCompleteInAsync(Timeouts.IpcRoundtrip);
    }

    [Theory, IpcAutoData]
    public async Task StreamDownloadsLeftOpen_WillHijackTheConnection(string str, Guid guid)
    {
        using (var stream = await Proxy.Download(str))
        {
            await new StreamReader(stream).ReadToEndAsync()
                .ShouldBeAsync(str);

            await Proxy.EchoGuidAfter(guid, waitOnServer: TimeSpan.Zero, message: new() { RequestTimeout = Timeout.InfiniteTimeSpan })
                .ShouldStallForAtLeastAsync(Timeouts.IpcRoundtrip);
        }
    }

#if !CI
    [Theory, IpcAutoData]
#endif
    public async Task StreamDownloadsLeftOpen_WillHijackTheConnection_Repeat(string str, Guid guid)
    {
        const int IterationCount = 20;
        foreach (var i in Enumerable.Range(0, IterationCount))
        {
            await StreamDownloadsLeftOpen_WillHijackTheConnection(str, guid);
        }
    }

    [Theory, IpcAutoData]
    public async Task IpcServerDispose_ShouldBeIdempotent(Guid guid)
    {
        await Proxy.EchoGuidAfter(guid, waitOnServer: default).ShouldBeAsync(guid);
        var infiniteTask = Proxy.EchoGuidAfter(guid, Timeout.InfiniteTimeSpan);

        using (var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddHostedSingleton<IHostedIpcServer, HostedIpcServer>())
            .Build())
        {
            await host.StartAsync();
            var hostedIpcServer = host.Services.GetRequiredService<IHostedIpcServer>();
            hostedIpcServer.Set(IpcServer!);
            await host.StopAsync();
        }

        await IpcServer!.DisposeAsync();
        await IpcServer!.DisposeAsync();
        await infiniteTask.ShouldThrowAsync<IOException>().ShouldCompleteInAsync(Timeouts.IpcRoundtrip);
    }

    private sealed class UploadStream : StreamBase
    {
        private readonly Channel<ReadCall> _readCalls = Channel.CreateUnbounded<ReadCall>();

        public byte? AutoRespondByte { get; set; }

        public async Task<ReadCall> AwaitReadCall(CancellationToken ct = default) => await _readCalls.Reader.ReadAsync(ct);

        public override long Length => long.MaxValue;
        public override bool CanRead => true;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (AutoRespondByte is { } @byte)
            {
                if (@byte > 0)
                {
                    buffer.AsSpan().Slice(offset, count).Fill(@byte);
                }

                return Task.FromResult(count);
            }

            var memory = new Memory<byte>(buffer, offset, count);
            var call = new ReadCall(out var task)
            {
                Memory = new(buffer, offset, count),
                CancellationToken = cancellationToken
            };

            if (!_readCalls.Writer.TryWrite(call))
            {
                throw new InvalidOperationException();
            }

            return task;
        }

        public sealed class ReadCall
        {
            public required Memory<byte> Memory { get; init; }
            public required CancellationToken CancellationToken { get; init; }

            private readonly TaskCompletionSource<int> _tcs = new();

            public ReadCall(out Task<int> task) => task = _tcs.Task;

            public void Return(int cbRead) => _tcs.TrySetResult(cbRead);
        }
    }

    private interface IHostedIpcServer
    {
        void Set(IpcServer ipcServer);
    }

    private sealed class HostedIpcServer : IHostedService, IHostedIpcServer, IAsyncDisposable
    {
        private IpcServer? _ipcServer;

        public void Set(IpcServer ipcServer) => _ipcServer = ipcServer;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _ipcServer!.DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _ipcServer!.DisposeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                throw;
            }
        }
    }
}