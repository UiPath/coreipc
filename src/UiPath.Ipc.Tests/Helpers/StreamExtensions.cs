namespace UiPath.Ipc.Tests;

public static class StreamExtensions
{
    public static async Task ReadExactlyAsync(this Stream stream, byte[] buffer, int offset, int length, CancellationToken ct = default)
    {
        while (length > 0)
        {
            var cbRead = await stream.ReadAsync(buffer, offset, length, ct);
            if (cbRead == 0)
            {
                throw new EndOfStreamException();
            }

            offset += cbRead;
            length -= cbRead;
        }
    }
}
