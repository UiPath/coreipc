using System.Text;
using System.Threading.Channels;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public abstract class SystemTests : TestBase
{
    #region " Setup "
    private readonly Lazy<SystemService> _service;
    private readonly Lazy<ISystemService> _proxy;

    protected SystemService Service => _service.Value;
    protected ISystemService Proxy => _proxy.Value;

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
    protected override ClientBase ConfigTransportAgnostic(ClientBase client)
    => client with
    {
        RequestTimeout = Timeouts.DefaultRequest,
        ServiceProvider = ServiceProvider
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
        var task1 = Proxy.EchoGuidAfter(guid1, Timeout.InfiniteTimeSpan, cts.Token);

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
        public override ListenerConfig Override(ListenerConfig listener) => listener with { RequestTimeout = Timeouts.Short };
        public override ClientBase Override(ClientBase client) => client with { RequestTimeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class ClientWaitingForTooLongACall_ShouldThrowTimeout_Config : OverrideConfig
    {
        public override ListenerConfig Override(ListenerConfig listener) => listener with { RequestTimeout = Timeout.InfiniteTimeSpan };
        public override ClientBase Override(ClientBase client) => client with { RequestTimeout = Timeouts.IpcRoundtrip };
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
        public override ClientBase Override(ClientBase client)
        => client with
        {
            Callbacks = new()
            {
                { typeof(IComputingCallback), new ComputingCallback() },
                { typeof(IArithmeticCallback), new ArithmeticCallback() },
            }
        };
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

    [Theory, IpcAutoData]
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

        await Proxy.EchoGuidAfter(guid, duration: TimeSpan.Zero) // we expect the connection to recover
            .ShouldBeAsync(guid);

        IpcProxy.Network.ShouldNotBeNull().ShouldNotBeSameAs(networkBeforeCancel); // and the network to be a new one
    }

    [Theory, IpcAutoData]
    public async Task UnfinishedUploads_ShouldThrowOnTheClient_AndRecover(Guid guid)
    {
        var stream = new UploadStream() { AutoRespondByte = 0 };

        await Proxy.UploadJustCountBytes(stream, serverReadByteCount: 1, TimeSpan.Zero) // the server method deliberately returns before finishing to read the entire stream
            .ShouldThrowAsync<Exception>();

        await Proxy.EchoGuidAfter(guid, TimeSpan.Zero)
            .ShouldBeAsync(guid);
    }

    [Theory, IpcAutoData]
    public async Task DownloadingStreams_ShouldWork(string str)
    {
        using var stream = await Proxy.Download(str);
        using var reader = new StreamReader(stream);
        var clone = await reader.ReadToEndAsync();
        clone.ShouldBe(str);
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

            await Proxy.EchoGuidAfter(guid, TimeSpan.Zero)
                .ShouldStallForAtLeastAsync(Timeouts.IpcRoundtrip + Timeouts.IpcRoundtrip);
        }
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
}
