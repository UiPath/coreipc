namespace UiPath.CoreIpc.Tests;

internal sealed class TracedStream(Stream target) : StreamBase
{
    private readonly MemoryStream _bytes = new();

    public byte[] GetTrace() => _bytes.ToArray();

    public override long Length => target.Length;
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var cbRead = await target.ReadAsync(buffer, offset, count, cancellationToken);
        _bytes.Write(buffer, offset, cbRead);  
        return cbRead;
    }
}
