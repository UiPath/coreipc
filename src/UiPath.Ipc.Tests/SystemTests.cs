using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace UiPath.Ipc.Tests;

public abstract class SystemTests : TestBase<ISystemService, SystemService>
{
    protected TListener CommonConfigListener<TListener>(TListener listener) where TListener : ListenerConfig
    => listener with
    {
        ConcurrentAccepts = 10,
        RequestTimeout = Debugger.IsAttached ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(2),
        MaxReceivedMessageSizeInMegabytes = 1,
    };

    protected TClient CommonConfigClient<TClient>(TClient client) where TClient : ClientBase => client;
    
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
        var act = () => task1.ShouldCompleteInAsync(Constants.Timeout_LocalProxyToThrowOCE);
        await act.ShouldThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NotPassingAnOptionalMessage_ShouldWork()
    => await Proxy
        .MessageReceivedAsNotNull(message: null)
        .ShouldCompleteInAsync(Constants.Timeout_IpcRoundtrip)
        .ShouldNotThrowAsyncAnd()
        .ShouldBeAsync(true);

    [Fact]
    public async Task ServerExecutingLongCall_ShouldThrowTimeout()
    => await Proxy.EchoGuidAfter(Guid.Empty, Timeout.InfiniteTimeSpan) // method takes forever but we have a server side RequestTimeout configured
        .ShouldThrowAsync<RemoteException>()
        .ShouldSatisfyAllConditionsAsync(
        [
            ex => ex.Message.ShouldBe(TimeoutHelper.ComputeTimeoutMessage(nameof(Proxy.EchoGuidAfter))),
            ex => ex.Is<TimeoutException>().ShouldBeTrue()
        ]);

    [Fact]
    public async Task FireAndForget_ShouldWork()
    {
        var taskRequestHonoured = Service.ResetTripWire();

        await Proxy.FireAndForget().ShouldCompleteInAsync(Constants.Timeout_IpcRoundtrip);
        taskRequestHonoured.IsCompleted.ShouldBeFalse();

        await taskRequestHonoured.ShouldCompleteInAsync(Constants.Timeout_IpcRoundtrip + TimeSpan.FromMilliseconds(SystemService.MsFireAndForgetDelay));
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
    public async Task FireAndForgetOperations_ShouldNotDeliverBusinessExceptionsEvenWhenThrownSynchronously()
    => await Proxy.FireAndForgetThrowSync()
        .ShouldNotThrowAsync()
        .ShouldCompleteInAsync(Constants.Timeout_IpcRoundtrip);

    [Fact]
    public async Task ServerScheduler_ShouldBeUsed()
    => await Proxy.GetThreadName()
        .ShouldBeAsync(Names.GuiThreadName);

    [Theory, IpcAutoData]
    public async Task UploadingStreams_ShouldWork(string str)
    {
        using var memory = new MemoryStream(Encoding.UTF8.GetBytes(str));
        await Proxy.UploadEcho(memory, trace: false).ShouldBeAsync(str);
    }

    [Theory, IpcAutoData]
    public async Task CancelingStreamUploads_ShouldThrow(string str)
    {
        var memory = new Memory<byte>(Encoding.UTF8.GetBytes(str));

        using var cts = new CancellationTokenSource();
        using var stream = new UploadStream();

        var taskReadCall = stream.AwaitReadCall();
        var taskUploading = Proxy.UploadEcho(stream, trace: true, cts.Token);

        var readCall = await taskReadCall.ShouldCompleteInAsync(Constants.Timeout_IpcRoundtrip);
        var cbRead = Math.Min(readCall.Memory.Length, memory.Length);
        memory.Slice(cbRead).CopyTo(readCall.Memory);
        readCall.Return(cbRead);

        taskUploading.IsCompleted.ShouldBeFalse();

        cts.Cancel();
        await taskUploading
            .ShouldThrowAsync<OperationCanceledException>()
            .ShouldCompleteInAsync(TimeSpan.FromSeconds(20));// Constants.Timeout_IpcRoundtrip + Constants.Timeout_IpcRoundtrip + Constants.Timeout_IpcRoundtrip);

        Service.LatestUploadTrace.ShouldNotBeNull();
        var expectedServerRead = Encoding.UTF8.GetString(memory.Slice(cbRead).ToArray());
        var act = () => Encoding.UTF8.GetString(Service.LatestUploadTrace);
        act.ShouldNotThrow().ShouldBe(expectedServerRead);
    }

    [Theory, IpcAutoData]
    public async Task UnfinishedUploads_ShouldThrowOnTheClient_AndRecover(Guid guid)
    {
        const int StreamLength = 100;

        using var memory = new MemoryStream();
        memory.SetLength(StreamLength);

        await Proxy.UploadJustCountBytes(memory, serverReadByteCount: StreamLength - 1, TimeSpan.Zero)
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
            .ShouldCompleteInAsync(Constants.Timeout_IpcRoundtrip);
    }

    [Theory, IpcAutoData]
    public async Task StreamDownloadsLeftOpen_WillHijackTheConnection(string str, Guid guid)
    {
        using (var stream = await Proxy.Download(str))
        {
            await new StreamReader(stream).ReadToEndAsync()
                .ShouldBeAsync(str);

            await Proxy.EchoGuidAfter(guid, TimeSpan.Zero)
                .ShouldStallForAtLeastAsync(Constants.Timeout_IpcRoundtrip + Constants.Timeout_IpcRoundtrip);
        }
    }

    private sealed class UploadStream : StreamBase
    {
        private readonly Channel<ReadCall> _readCalls = Channel.CreateUnbounded<ReadCall>();

        public async Task<ReadCall> AwaitReadCall(CancellationToken ct = default) => await _readCalls.Reader.ReadAsync(ct);

        public override long Length => long.MaxValue;
        public override bool CanRead => true;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
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
