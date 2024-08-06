using System.Buffers;
using System.Text;

namespace UiPath.Ipc.Tests;

public sealed class SystemService : ISystemService
{
    public async Task<Guid> EchoGuidAfter(Guid value, TimeSpan duration, CancellationToken ct = default)
    {
        await Task.Delay(duration, ct);
        return value;
    }

    public async Task<bool> MessageReceivedAsNotNull(Message? message = null)
    => message is not null;

    private volatile TaskCompletionSource<object?>? _tripWire = null;
    internal Task ResetTripWire() => (_tripWire = new()).Task;
    public const int MsFireAndForgetDelay =
#if CI
            400;
#else
            40;
#endif
    public async Task FireAndForget()
    {
        await Task.Delay(MsFireAndForgetDelay);
        _tripWire?.TrySetResult(null);
    }

    public Task<string> EchoString(string value) => Task.FromResult(value);

    public async Task<(string ExceptionType, string ExceptionMessage, string? MarshalledExceptionType)?> CallUnregisteredCallback(Message message = null!)
    {
        try
        {
            _ = await message.GetCallback<IUnregisteredCallback>().SomeMethod();
            return null;
        }
        catch (Exception ex)
        {
            return (ex.GetType().Name, ex.Message, (ex as RemoteException)?.Type);
        }
    }

    public Task FireAndForgetThrowSync() => throw null!;

    public async Task<string?> GetThreadName() => Thread.CurrentThread.Name;


    private readonly object _latestUploadTraceLock = new();
    private TaskCompletionSource<byte[]>? _latestUploadTrace;
    internal Task<byte[]> ResetLatestUploadTrace()
    {
        lock (_latestUploadTraceLock)
        {
            return (_latestUploadTrace = new()).Task;            
        }
    }
    private void TrySetLatestUploadTraceResult(byte[] bytes)
    {
        lock (_latestUploadTraceLock)
        {
            _latestUploadTrace?.TrySetResult(bytes);
        }
    }

    public async Task<string> UploadEcho(Stream stream, bool trace, CancellationToken ct = default)
    {
        if (trace)
        {
            return await WithTrace(Logic);
        }

        return await Logic(stream);

        async Task<string> Logic(Stream stream)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
        async Task<T> WithTrace<T>(Func<Stream, Task<T>> asyncFunc)
        {
            var tracedStream = new TracedStream(stream);
            try
            {
                return await asyncFunc(tracedStream);
            }
            finally
            {
                TrySetLatestUploadTraceResult(tracedStream.GetTrace());
            }
        }
    }

    public async Task<bool> UploadJustCountBytes(Stream stream, int serverReadByteCount, TimeSpan serverDelay, CancellationToken ct = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(serverReadByteCount);
        try
        {
            await Task.Delay(serverDelay, ct);
            await stream.ReadExactlyAsync(buffer, 0, serverReadByteCount, ct);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<Stream> Download(string s, CancellationToken ct = default)
    => new MemoryStream(Encoding.UTF8.GetBytes(s));
}
