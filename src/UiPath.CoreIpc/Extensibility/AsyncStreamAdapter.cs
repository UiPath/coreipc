namespace UiPath.Ipc.Extensibility;

internal sealed class AsyncStreamAdapter : Stream
{
    private readonly IAsyncStream _target;

    public AsyncStreamAdapter(IAsyncStream stream)
    {
        _target = stream;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _target.DisposeAsync().AsTask().WaitAndUnwrapException();
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    => _target.Read(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    => _target.Write(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();
    public override Task FlushAsync(CancellationToken cancellationToken)
    => _target.Flush(cancellationToken).AsTask();

    public override void Flush() => throw new NotImplementedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
    public override void SetLength(long value) => throw new NotImplementedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    public override long Length => throw new NotImplementedException();
    public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
}

