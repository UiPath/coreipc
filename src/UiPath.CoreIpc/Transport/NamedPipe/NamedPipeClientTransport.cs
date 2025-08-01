﻿using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.Transport.NamedPipe;

public record NamedPipeClientTransport : ClientTransport
{
    public required string PipeName { get; set; }
    public string ServerName { get; set; } = ".";
    public bool AllowImpersonation { get; set; }

    public override string ToString() => $"ClientPipe={PipeName}";

    internal override IClientState CreateState() => new NamedPipeClientState();

    internal override void Validate()
    {
        if (PipeName is null or "")
        {
            throw new InvalidOperationException($"{nameof(PipeName)} is required.");
        }
        if (ServerName is null or "")
        {
            throw new InvalidOperationException($"{nameof(ServerName)} is required.");
        }
    }
}

internal class NamedPipeClientState : IClientState
{
    protected NamedPipeClientStream? _pipe;

    public virtual Stream? Network => _pipe;
    public bool IsConnected() => _pipe?.IsConnected is true;

    public virtual async ValueTask Connect(IpcClient client, CancellationToken ct)
    {
        var transport = client.Transport as NamedPipeClientTransport ?? throw new InvalidOperationException();

        _pipe = new NamedPipeClientStream(
            transport.ServerName,
            transport.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            transport.AllowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);

        await _pipe.ConnectAsync(ct);
    }

    public void Dispose() => _pipe?.Dispose();
}
