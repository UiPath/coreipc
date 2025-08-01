﻿using Castle.Core;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace UiPath.Ipc.Tests;

public sealed class SystemService : ISystemService
{
    public async Task<Guid> EchoGuidAfter(Guid value, TimeSpan waitOnServer, Message? message = null, CancellationToken ct = default)
    {
        await Task.Delay(waitOnServer, ct);
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
    public async Task FireAndForget(TimeSpan wait)
    {
        await Task.Delay(wait);
        _tripWire?.TrySetResult(null);
    }

    public Task<string> EchoString(string value) => Task.FromResult(value);

    public async Task<(string ExceptionType, string ExceptionMessage, string? MarshalledExceptionType)?> CallUnregisteredCallback(Message message = null!)
    {
        try
        {
            _ = await message.Client.GetCallback<IUnregisteredCallback>().SomeMethod();
            return null;
        }
        catch (Exception ex)
        {
            return (ex.GetType().Name, ex.Message, (ex as RemoteException)?.Type);
        }
    }

    public Task FireAndForgetThrowSync() => throw new MarkerException();

    public sealed class MarkerException : Exception { }

    public async Task<string?> GetThreadName() => Thread.CurrentThread.Name;

    public async Task<string> UploadEcho(Stream stream, CancellationToken ct = default)
    {
        var bytes = await stream.ReadToEndAsync(ct);
        return Encoding.UTF8.GetString(bytes);
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

    public async Task<int> AddIncrement(int x, int y, Message message = null!)
    {
        var sum = await message.Client.GetCallback<IComputingCallbackBase>().AddInts(x, y);
        var result = await message.Client.GetCallback<IArithmeticCallback>().Increment(sum);
        return result;
    }

    public async Task FireAndForgetWithCt(CancellationToken ct)
    {
        await Task.Delay(100, ct);
    }

    private static readonly Lazy<CultureInfo> Danish = new(() => new("da-DK"));

    public Task<string> DanishNameOfDay(DayOfWeek day, CancellationToken ct)
    => Task.FromResult(Danish.Value.DateTimeFormat.GetDayName(day));

    public Task<byte[]> ReverseBytes(byte[] bytes, CancellationToken ct = default)
    {
        for (int i = 0, j = bytes.Length - 1; i < j; i++, j--)
        {
            var t = bytes[i];
            bytes[i] = bytes[j];
            bytes[j] = t;
        }
        return Task.FromResult(bytes);
    }
}
