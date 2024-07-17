using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.Transport.NamedPipe;

using static NamedPipeKey;

public sealed record NamedPipeKey : IConnectionKey<NamedPipeKey, NamedPipeClientConnectionState>
{
    public required string PipeName { get; init; }
    public string ServerName { get; init; } = ".";
    public bool AllowImpersonation { get; init; } = false;

    public required ConnectionConfig DefaultConfig { get; init; }

    public async Task<OneOf<IAsyncStream, Stream>> Connect(CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(
            ServerName, 
            PipeName, 
            PipeDirection.InOut, 
            PipeOptions.Asynchronous, 
            AllowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(ct);
        return pipe;
    }

    private sealed class NamedPipeClientConnectionState
    {
        public required NamedPipeClientStream Stream { get; init; }
    }
}
