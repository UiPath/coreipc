using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace UiPath.CoreIpc
{
    /// <summary>
    /// A stream that allows for reading from another stream up to a given number of bytes.
    /// https://github.com/AArnott/Nerdbank.Streams/blob/3303c541c29b979f61c86c3c2ed5c0e7372d7a55/src/Nerdbank.Streams/NestedStream.cs#L18
    /// </summary>
    public class NestedStream : Stream
    {
        /// <summary>
        /// The stream to read from.
        /// </summary>
        private Stream _underlyingStream;
        /// <summary>
        /// The total length of the stream.
        /// </summary>
        private long _length;
        /// <summary>
        /// The remaining bytes allowed to be read.
        /// </summary>
        private long _remainingBytes;
        /// <summary>
        /// Initializes a new instance of the <see cref="NestedStream"/> class.
        /// </summary>
        /// <param name="underlyingStream">The stream to read from.</param>
        /// <param name="length">The number of bytes to read from the parent stream.</param>
        public NestedStream(Stream underlyingStream, long length)
        {
            _underlyingStream = underlyingStream;
            Reset(length);
        }

        public void Reset(long length)
        {
            _remainingBytes = length;
            _length = length;
        }

        public event EventHandler Disposed;
        /// <inheritdoc />
        public bool IsDisposed => _underlyingStream == null;
        /// <inheritdoc />
        public override bool CanRead => !IsDisposed;
        /// <inheritdoc />
        public override bool CanSeek => !IsDisposed && _underlyingStream.CanSeek;
        /// <inheritdoc />
        public override bool CanWrite => false;
        /// <inheritdoc />
        public override long Length => _length;
        /// <inheritdoc />
        public override long Position
        {
            get => _length - _remainingBytes;
            set => Seek(value, SeekOrigin.Begin);
        }
        /// <inheritdoc />
        public override void Flush() => throw new NotSupportedException();
        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if ((count = BytesToRead(buffer, offset, count)) <= 0)
            {
                return 0;
            }
            int bytesRead = await _underlyingStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            _remainingBytes -= bytesRead;
            return bytesRead;
        }
        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            if ((count = BytesToRead(buffer, offset, count)) <= 0)
            {
                return 0;
            }
            int bytesRead = _underlyingStream.Read(buffer, offset, count);
            _remainingBytes -= bytesRead;
            return bytesRead;
        }
        private int BytesToRead(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (offset < 0 || count < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (offset + count > buffer.Length)
            {
                throw new ArgumentException();
            }
            return (int)Math.Min(count, _remainingBytes);
        }
        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!CanSeek)
            {
                throw new NotSupportedException();
            }
            // Recalculate offset relative to the current position
            long newOffset = origin switch
            {
                SeekOrigin.Current => offset,
                SeekOrigin.End => _length + offset - Position,
                SeekOrigin.Begin => offset - Position,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            // Determine whether the requested position is within the bounds of the stream
            if (Position + newOffset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            long currentPosition = _underlyingStream.Position;
            long newPosition = _underlyingStream.Seek(newOffset, SeekOrigin.Current);
            _remainingBytes -= newPosition - currentPosition;
            return Position;
        }
        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (_remainingBytes != 0)
            {
                _underlyingStream?.Dispose();
                _underlyingStream = null;
            }
            Disposed?.Invoke(this, EventArgs.Empty);
            base.Dispose(disposing);
        }
    }
}