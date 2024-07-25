using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.Transport.NamedPipe;

public sealed class NamedPipeClientState : IClientState<NamedPipeClient, NamedPipeClientState>
{
    private NamedPipeClientStream? _pipe;

    public Network? Network => _pipe;
    public bool IsConnected() => _pipe?.IsConnected is true;

    public async ValueTask Connect(NamedPipeClient client, CancellationToken ct)
    {
        _pipe = new NamedPipeClientStream(
            client.ServerName,
            client.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            client.AllowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);
        await _pipe.ConnectAsync(ct);
    }
}

public sealed record NamedPipeClient : ClientBase, IClient<NamedPipeClientState, NamedPipeClient>
{
    public required string PipeName { get; init; }
    public string ServerName { get; init; } = ".";
    public bool AllowImpersonation { get; init; } = false;
}
