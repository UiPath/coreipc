﻿using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.Transport.NamedPipe;

public sealed record NamedPipeTransport : ClientTransport
{
    public required string PipeName { get; init; }
    public string ServerName { get; init; } = ".";
    public bool AllowImpersonation { get; init; }

    public override string ToString() => $"ClientPipe={PipeName}";

    public override IClientState CreateState() => new NamedPipeClientState();

    public override void Validate()
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

internal sealed class NamedPipeClientState : IClientState
{
    private NamedPipeClientStream? _pipe;

    public Network? Network => _pipe;
    public bool IsConnected() => _pipe?.IsConnected is true;

    public async ValueTask Connect(IpcClient client, CancellationToken ct)
    {
        var transport = client.Transport as NamedPipeTransport ?? throw new InvalidOperationException();

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
