using System.Net.WebSockets;

namespace UiPath.Ipc.Transport.WebSocket;

using WebSocket = System.Net.WebSockets.WebSocket;

/// <summary>
/// Exposes a <see cref="WebSock"/> as a <see cref="Stream"/>.
/// https://github.com/AArnott/Nerdbank.Streams/blob/main/src/Nerdbank.Streams/WebSocketStream.cs
/// </summary>
internal class WebSocketStream : Stream
{
    /// <summary>
    /// The socket wrapped by this stream.
    /// </summary>
    private readonly WebSocket _webSocket;
    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketStream"/> class.
    /// </summary>
    /// <param name="webSocket">The web socket to wrap in a stream.</param>
    public WebSocketStream(WebSocket webSocket) => _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
    /// <inheritdoc />
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_webSocket.CloseStatus.HasValue)
        {
            return 0;
        }
        var result = await _webSocket.ReceiveAsync(new(buffer, offset, count), cancellationToken).ConfigureAwait(false);
        return result.Count;
    }
    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _webSocket.SendAsync(new(buffer, offset, count), WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
    /// <summary>
    /// Does nothing, since web sockets do not need to be flushed.
    /// </summary>
    public override void Flush(){}
    /// <summary>
    /// Does nothing, since web sockets do not need to be flushed.
    /// </summary>
    /// <param name="cancellationToken">An ignored cancellation token.</param>
    /// <returns>A completed task.</returns>
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _webSocket.Dispose();
        base.Dispose(disposing);
    }
    /// <inheritdoc />
    public override bool CanRead => true;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => true;
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
}