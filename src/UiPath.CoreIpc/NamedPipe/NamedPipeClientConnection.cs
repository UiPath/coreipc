using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.NamedPipe;

public class NamedPipeClientConnection : ClientConnection
{
    private NamedPipeConnectionKey Key => ConnectionKey as NamedPipeConnectionKey ?? throw new InvalidOperationException();
    private NamedPipeClientStream? _pipe;

    protected override void Dispose(bool disposing)
    {
        _pipe?.Dispose();
        base.Dispose(disposing);
    }

    public override bool Connected => _pipe is { IsConnected: true };

    public override async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        _pipe = new(Key.ServerName, Key.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, Key.AllowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);
        await _pipe.ConnectAsync(cancellationToken);
        return _pipe;
    }
}
