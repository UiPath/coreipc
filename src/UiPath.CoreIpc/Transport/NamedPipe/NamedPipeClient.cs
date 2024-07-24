using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.Transport.NamedPipe;

public sealed record NamedPipeClient : ClientBase
{
    public required string PipeName { get; init; }
    public string ServerName { get; init; } = ".";
    public bool AllowImpersonation { get; init; } = false;

    protected internal override async Task<Network> Connect(CancellationToken ct)
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
}

//public sealed record NamedPipeKey : IConnectionKey<NamedPipeKey, NamedPipeClientConnectionState>
//{
//    public required ClientBase DefaultConfig { get; init; }

//    public async Task<Network> Connect(CancellationToken ct)
//    {
//        var pipe = new NamedPipeClientStream(
//            ServerName, 
//            PipeName, 
//            PipeDirection.InOut, 
//            PipeOptions.Asynchronous, 
//            AllowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);
//        await pipe.ConnectAsync(ct);
//        return pipe;
//    }

//    private sealed class NamedPipeClientConnectionState
//    {
//        public required NamedPipeClientStream Stream { get; init; }
//    }
//}
