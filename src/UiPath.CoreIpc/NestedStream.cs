﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    /// <summary>
    /// A stream that allows for reading from another stream up to a given number of bytes.
    /// </summary>
    internal class NestedStream : Stream
    {
        /// <summary>
        /// The stream to read from.
        /// </summary>
        private readonly Stream underlyingStream;

        /// <summary>
        /// The total length of the stream.
        /// </summary>
        private readonly long length;

        /// <summary>
        /// The remaining bytes allowed to be read.
        /// </summary>
        private long remainingBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="NestedStream"/> class.
        /// </summary>
        /// <param name="underlyingStream">The stream to read from.</param>
        /// <param name="length">The number of bytes to read from the parent stream.</param>
        public NestedStream(Stream underlyingStream, long length)
        {
            this.underlyingStream = underlyingStream;
            this.remainingBytes = length;
            this.length = length;
        }

        public event EventHandler Disposed;

        /// <inheritdoc />
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public override bool CanRead => !this.IsDisposed;

        /// <inheritdoc />
        public override bool CanSeek => !this.IsDisposed && this.underlyingStream.CanSeek;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length
        {
            get
            {
                return this.underlyingStream.CanSeek ?
                    this.length : throw new NotSupportedException();
            }
        }

        /// <inheritdoc />
        public override long Position
        {
            get
            {
                return this.length - this.remainingBytes;
            }

            set
            {
                this.Seek(value, SeekOrigin.Begin);
            }
        }

        /// <inheritdoc />
        public override void Flush() => throw new NotSupportedException();

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
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

            count = (int)Math.Min(count, this.remainingBytes);

            if (count <= 0)
            {
                return 0;
            }

            int bytesRead = await this.underlyingStream.ReadAsync(buffer, offset, count).ConfigureAwait(false);
            this.remainingBytes -= bytesRead;
            return bytesRead;
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
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

            count = (int)Math.Min(count, this.remainingBytes);

            if (count <= 0)
            {
                return 0;
            }

            int bytesRead = this.underlyingStream.Read(buffer, offset, count);
            this.remainingBytes -= bytesRead;
            return bytesRead;
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!this.CanSeek)
            {
                throw new NotSupportedException();
            }

            // Recalculate offset relative to the current position
            long newOffset = origin switch
            {
                SeekOrigin.Current => offset,
                SeekOrigin.End => this.length + offset - this.Position,
                SeekOrigin.Begin => offset - this.Position,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            // Determine whether the requested position is within the bounds of the stream
            if (this.Position + newOffset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            long currentPosition = this.underlyingStream.Position;
            long newPosition = this.underlyingStream.Seek(newOffset, SeekOrigin.Current);
            this.remainingBytes -= newPosition - currentPosition;
            return this.Position;
        }

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            Disposed?.Invoke(this, EventArgs.Empty);
            base.Dispose(disposing);
        }
    }
}