#pragma warning disable

#if (NETFRAMEWORK || NETSTANDARD2_0 || NETCOREAPP2_0)

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Link = System.ComponentModel.DescriptionAttribute;

internal static class StreamExtensions
{
    /// <summary>
    /// Asynchronously reads a sequence of bytes from the current stream, advances the position within the stream by
    /// the number of bytes read, and monitors cancellation requests.
    /// </summary>
    /// <param name="buffer">The region of memory to write the data into.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous read operation. The value of its Result property contains the
    /// total number of bytes read into the buffer. The result value can be less than the number of bytes allocated in
    /// the buffer if that many bytes are not currently available, or it can be 0 (zero) if the end of the stream has
    /// been reached.
    /// </returns>
    [Link("https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.readasync#system-io-stream-readasync(system-memory((system-byte))-system-threading-cancellationtoken)")]
    public static ValueTask<int> ReadAsync(
        this Stream target,
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (!MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)buffer, out var segment))
        {
            segment = new(buffer.ToArray());
        }

        return new(target.ReadAsync(segment.Array!, segment.Offset, segment.Count, cancellationToken));
    }
}

#endif
