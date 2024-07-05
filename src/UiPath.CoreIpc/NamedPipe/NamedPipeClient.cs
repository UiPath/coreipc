using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.NamedPipe;

internal class NamedPipeClient<TInterface> : ServiceClient<TInterface> where TInterface : class
{
    private readonly NamedPipeConnectionKey _key;

    public NamedPipeClient(NamedPipeConnectionKey key, ConnectionConfig config) : base(config, key)
    {
        _key = key;
    }

    public override string DebugName => base.DebugName ?? _key.PipeName;
}

internal class NamedPipeClientConnection : ClientConnection
{
    private readonly NamedPipeConnectionKey _key;
    private NamedPipeClientStream? _pipe;

    public NamedPipeClientConnection(NamedPipeConnectionKey key) : base(key)
    {
        _key = key;
    }
    protected override void Dispose(bool disposing)
    {
        _pipe?.Dispose();
        base.Dispose(disposing);
    }

    public override bool Connected => _pipe is { IsConnected: true };

    public override async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        _pipe = new(_key.ServerName, _key.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, _key.AllowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);
        await _pipe.ConnectAsync(cancellationToken);
        return _pipe;
    }
}
