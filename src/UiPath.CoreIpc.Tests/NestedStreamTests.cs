using Shouldly;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace UiPath.CoreIpc.Tests;

public class NestedStreamTests
{
    private const int DefaultNestedLength = 10;

    private MemoryStream underlyingStream;

    private NestedStream stream;

    protected static readonly TimeSpan UnexpectedTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _timeoutTokenSource = new(UnexpectedTimeout);

    public NestedStreamTests()
    {
        var random = new Random();
        var buffer = new byte[20];
        random.NextBytes(buffer);
        this.underlyingStream = new MemoryStream(buffer);
        this.stream = this.underlyingStream.ReadSlice(DefaultNestedLength);
    }

    protected CancellationToken TimeoutToken => Debugger.IsAttached ? CancellationToken.None : _timeoutTokenSource.Token;

    [Fact]
    public void CanSeek()
    {
        Assert.True(this.stream.CanSeek);
        this.stream.Dispose();
        Assert.False(this.stream.CanSeek);
    }

    [Fact]
    public void CanSeek_NonSeekableStream()
    {
        using var gzipStream = new GZipStream(Stream.Null, CompressionMode.Decompress);
        using var stream = gzipStream.ReadSlice(10);

        Assert.False(stream.CanSeek);
        stream.Dispose();
        Assert.False(stream.CanSeek);
    }

    [Fact]
    public void Length()
    {
        Assert.Equal(DefaultNestedLength, this.stream.Length);
    }

    [Fact]
    public void Length_NonSeekableStream()
    {
        using (var gzipStream = new GZipStream(Stream.Null, CompressionMode.Decompress))
        using (var stream = gzipStream.ReadSlice(10))
        {
            stream.Length.ShouldBe(10);
        }
    }

    [Fact]
    public void Position()
    {
        byte[] buffer = new byte[DefaultNestedLength];

        Assert.Equal(0, this.stream.Position);
        var bytesRead = this.stream.Read(buffer, 0, 5);
        Assert.Equal(bytesRead, this.stream.Position);

        this.stream.Position = 0;
        byte[] buffer2 = new byte[DefaultNestedLength];
        bytesRead = this.stream.Read(buffer2, 0, 5);
        Assert.Equal(bytesRead, this.stream.Position);
        Assert.Equal<byte>(buffer, buffer2);
    }

    [Fact]
    public void Position_NonSeekableStream()
    {
        using var nonSeekableWrapper = new OneWayStreamWrapper(this.underlyingStream, canRead: true);
        using var stream = nonSeekableWrapper.ReadSlice(10);

        Assert.Equal(0, stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Position = 3);
        Assert.Equal(0, stream.Position);
        stream.ReadByte();
        Assert.Equal(1, stream.Position);
    }

    [Fact]
    public void IsDisposed()
    {
        Assert.False(stream.IsDisposed);
        this.stream.Dispose();
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public void Dispose_IncompleteDisposesUnderylingStream()
    {
        this.stream.Dispose();
        Assert.False(this.underlyingStream.CanSeek);
    }

    [Fact]
    public void Dispose_DoesNotDisposeUnderylingStream()
    {
        this.stream.Read(new byte[DefaultNestedLength], 0, DefaultNestedLength);
        this.stream.Dispose();
        Assert.True(this.underlyingStream.CanSeek);
        // A sanity check that if it were disposed, our assertion above would fail.
        this.underlyingStream.Dispose();
        Assert.False(this.underlyingStream.CanSeek);
    }

    [Fact]
    public void SetLength()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.SetLength(0));
    }

    [Fact]
    public void Seek_Current()
    {
        Assert.Equal(0, this.stream.Position);
        Assert.Equal(0, this.stream.Seek(0, SeekOrigin.Current));
        Assert.Equal(0, this.underlyingStream.Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Seek(-1, SeekOrigin.Current));
        Assert.Equal(0, this.underlyingStream.Position);

        Assert.Equal(5, this.stream.Seek(5, SeekOrigin.Current));
        Assert.Equal(5, this.underlyingStream.Position);
        Assert.Equal(5, this.stream.Seek(0, SeekOrigin.Current));
        Assert.Equal(5, this.underlyingStream.Position);
        Assert.Equal(4, this.stream.Seek(-1, SeekOrigin.Current));
        Assert.Equal(4, this.underlyingStream.Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Seek(-10, SeekOrigin.Current));
        Assert.Equal(4, this.underlyingStream.Position);

        Assert.Equal(0, this.stream.Seek(0, SeekOrigin.Begin));
        Assert.Equal(0, this.stream.Position);

        Assert.Equal(DefaultNestedLength + 1, this.stream.Seek(DefaultNestedLength + 1, SeekOrigin.Current));
        Assert.Equal(DefaultNestedLength + 1, this.underlyingStream.Position);
        Assert.Equal((2 * DefaultNestedLength) + 1, this.stream.Seek(DefaultNestedLength, SeekOrigin.Current));
        Assert.Equal((2 * DefaultNestedLength) + 1, this.underlyingStream.Position);
        Assert.Equal((2 * DefaultNestedLength) + 1, this.stream.Seek(0, SeekOrigin.Current));
        Assert.Equal((2 * DefaultNestedLength) + 1, this.underlyingStream.Position);
        Assert.Equal(1, this.stream.Seek(-2 * DefaultNestedLength, SeekOrigin.Current));
        Assert.Equal(1, this.underlyingStream.Position);

        this.stream.Dispose();
        Assert.Throws<NotSupportedException>(() => this.stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void Sook_WithNonStartPositionInUnderlyingStream()
    {
        this.underlyingStream.Position = 1;
        this.stream = this.underlyingStream.ReadSlice(5);

        Assert.Equal(0, this.stream.Position);
        Assert.Equal(2, this.stream.Seek(2, SeekOrigin.Current));
        Assert.Equal(3, this.underlyingStream.Position);
    }

    [Fact]
    public void Seek_Begin()
    {
        Assert.Equal(0, this.stream.Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Seek(-1, SeekOrigin.Begin));
        Assert.Equal(0, this.underlyingStream.Position);

        Assert.Equal(0, this.stream.Seek(0, SeekOrigin.Begin));
        Assert.Equal(0, this.underlyingStream.Position);

        Assert.Equal(5, this.stream.Seek(5, SeekOrigin.Begin));
        Assert.Equal(5, this.underlyingStream.Position);

        Assert.Equal(DefaultNestedLength, this.stream.Seek(DefaultNestedLength, SeekOrigin.Begin));
        Assert.Equal(DefaultNestedLength, this.underlyingStream.Position);

        Assert.Equal(DefaultNestedLength + 1, this.stream.Seek(DefaultNestedLength + 1, SeekOrigin.Begin));
        Assert.Equal(DefaultNestedLength + 1, this.underlyingStream.Position);

        this.stream.Dispose();
        Assert.Throws<NotSupportedException>(() => this.stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void Seek_End()
    {
        Assert.Equal(0, this.stream.Position);
        Assert.Equal(9, this.stream.Seek(-1, SeekOrigin.End));
        Assert.Equal(9, this.underlyingStream.Position);

        Assert.Equal(DefaultNestedLength, this.stream.Seek(0, SeekOrigin.End));
        Assert.Equal(DefaultNestedLength, this.underlyingStream.Position);

        Assert.Equal(DefaultNestedLength + 5, this.stream.Seek(5, SeekOrigin.End));
        Assert.Equal(DefaultNestedLength + 5, this.underlyingStream.Position);

        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Seek(-20, SeekOrigin.Begin));
        Assert.Equal(DefaultNestedLength + 5, this.underlyingStream.Position);

        this.stream.Dispose();
        Assert.Throws<NotSupportedException>(() => this.stream.Seek(0, SeekOrigin.End));
    }

    [Fact]
    public void Flush()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Flush());
    }

    [Fact]
    public async Task FlushAsync()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => this.stream.FlushAsync());
    }

    [Fact]
    public void CanRead()
    {
        Assert.True(this.stream.CanRead);
        this.stream.Dispose();
        Assert.False(this.stream.CanRead);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.False(this.stream.CanWrite);
        this.stream.Dispose();
        Assert.False(this.stream.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_Throws()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => this.stream.WriteAsync(new byte[1], 0, 1));
    }

    [Fact]
    public void Write_Throws()
    {
        Assert.Throws<NotSupportedException>(() => this.stream.Write(new byte[1], 0, 1));
    }

    [Fact]
    public async Task ReadAsync_Empty_ReturnsZero()
    {
        Assert.Equal(0, await this.stream.ReadAsync(Array.Empty<byte>(), 0, 0, default));
    }

    [Fact]
    public async Task Read_BeyondEndOfStream_ReturnsZero()
    {
        // Seek beyond the end of the stream
        this.stream.Seek(1, SeekOrigin.End);

        byte[] buffer = new byte[this.underlyingStream.Length];

        Assert.Equal(0, await this.stream.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken));
    }

    [Fact]
    public async Task ReadAsync_NoMoreThanGiven()
    {
        byte[] buffer = new byte[this.underlyingStream.Length];
        int bytesRead = await this.stream.ReadAsync(buffer, 0, buffer.Length, this.TimeoutToken);
        Assert.Equal(DefaultNestedLength, bytesRead);

        Assert.Equal(0, await this.stream.ReadAsync(buffer, bytesRead, buffer.Length - bytesRead, this.TimeoutToken));
        Assert.Equal(DefaultNestedLength, this.underlyingStream.Position);
    }

    [Fact]
    public void Read_NoMoreThanGiven()
    {
        byte[] buffer = new byte[this.underlyingStream.Length];
        int bytesRead = this.stream.Read(buffer, 0, buffer.Length);
        Assert.Equal(DefaultNestedLength, bytesRead);

        Assert.Equal(0, this.stream.Read(buffer, bytesRead, buffer.Length - bytesRead));
        Assert.Equal(DefaultNestedLength, this.underlyingStream.Position);
    }

    [Fact]
    public void Read_Empty_ReturnsZero()
    {
        Assert.Equal(0, this.stream.Read(Array.Empty<byte>(), 0, 0));
    }

    [Fact]
    public async Task ReadAsync_WhenLengthIsInitially0()
    {
        this.stream = this.underlyingStream.ReadSlice(0);
        Assert.Equal(0, await this.stream.ReadAsync(new byte[1], 0, 1, this.TimeoutToken));
    }

    [Fact]
    public void Read_WhenLengthIsInitially0()
    {
        this.stream = this.underlyingStream.ReadSlice(0);
        Assert.Equal(0, this.stream.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void CreationDoesNotReadFromUnderlyingStream()
    {
        Assert.Equal(0, this.underlyingStream.Position);
    }

    [Fact]
    public void Read_UnderlyingStreamReturnsFewerBytesThanRequested()
    {
        var buffer = new byte[20];
        int firstBlockLength = DefaultNestedLength / 2;
        this.underlyingStream.SetLength(firstBlockLength);
        Assert.Equal(firstBlockLength, this.stream.Read(buffer, 0, buffer.Length));
        this.underlyingStream.SetLength(DefaultNestedLength * 2);
        Assert.Equal(DefaultNestedLength - firstBlockLength, this.stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public async Task ReadAsync_UnderlyingStreamReturnsFewerBytesThanRequested()
    {
        var buffer = new byte[20];
        int firstBlockLength = DefaultNestedLength / 2;
        this.underlyingStream.SetLength(firstBlockLength);
        Assert.Equal(firstBlockLength, await this.stream.ReadAsync(buffer, 0, buffer.Length));
        this.underlyingStream.SetLength(DefaultNestedLength * 2);
        Assert.Equal(DefaultNestedLength - firstBlockLength, await this.stream.ReadAsync(buffer, 0, buffer.Length));
    }

    [Fact]
    public void Read_ValidatesArguments()
    {
        var buffer = new byte[20];

        Assert.Throws<ArgumentNullException>(() => this.stream.Read(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Read(buffer, -1, buffer.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream.Read(buffer, 0, -1));
        Assert.Throws<ArgumentException>(() => this.stream.Read(buffer, 1, buffer.Length));
    }

    [Fact]
    public async Task ReadAsync_ValidatesArguments()
    {
        var buffer = new byte[20];

        await Assert.ThrowsAsync<ArgumentNullException>(() => this.stream.ReadAsync(null!, 0, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.stream.ReadAsync(buffer, -1, buffer.Length));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.stream.ReadAsync(buffer, 0, -1));
        await Assert.ThrowsAsync<ArgumentException>(() => this.stream.ReadAsync(buffer, 1, buffer.Length));
    }
}
public static class StreamExtensions
{
    /// <summary>
    /// Creates a <see cref="Stream"/> that can read no more than a given number of bytes from an underlying stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="length">The number of bytes to read from the parent stream.</param>
    /// <returns>A stream that ends after <paramref name="length"/> bytes are read.</returns>
    public static NestedStream ReadSlice(this Stream stream, long length) => new(stream, length);
}