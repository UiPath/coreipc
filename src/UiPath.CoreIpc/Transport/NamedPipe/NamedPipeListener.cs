using Newtonsoft.Json;
using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.Transport.NamedPipe;

using INamedPipeListenerConfig = IListenerConfig<NamedPipeListener, NamedPipeListenerState, NamedPipeServerConnectionState>;

public sealed record NamedPipeListener : ListenerConfig, INamedPipeListenerConfig
{
    public required string PipeName { get; init; }
    public string ServerName { get; init; } = ".";
    [JsonIgnore]
    public AccessControlDelegate? AccessControl { get; init; }

    private PipeSecurity? GetPipeSecurity()
    {
        var setAccessControl = AccessControl;
        if (setAccessControl is null)
        {
            return null;
        }

        var pipeSecurity = new PipeSecurity();
        FullControlFor(WellKnownSidType.BuiltinAdministratorsSid);
        FullControlFor(WellKnownSidType.LocalSystemSid);
        pipeSecurity.AllowCurrentUser(onlyNonAdmin: true);
        setAccessControl(pipeSecurity);
        return pipeSecurity;
        void FullControlFor(WellKnownSidType sid) => pipeSecurity.Allow(sid, PipeAccessRights.FullControl);
    }

    NamedPipeListenerState INamedPipeListenerConfig.CreateListenerState(IpcServer server)
    => new();

    NamedPipeServerConnectionState INamedPipeListenerConfig.CreateConnectionState(IpcServer server, NamedPipeListenerState listenerState)
    => new()
    {
        Stream = IOHelpers.NewNamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            GetPipeSecurity)
    };

    async ValueTask<Stream> INamedPipeListenerConfig.AwaitConnection(NamedPipeListenerState listenerState, NamedPipeServerConnectionState connectionState, CancellationToken ct)
    {
        await connectionState.Stream.WaitForConnectionAsync(ct);
        return connectionState.Stream;
    }

    IEnumerable<string> INamedPipeListenerConfig.Validate()
    {
        if (PipeName is null or "") { yield return "PipeName is required"; }
    }

    public override string ToString() => $"ServerPipe={PipeName}";
}

internal sealed class NamedPipeServerConnectionState : IDisposable
{
    public required NamedPipeServerStream Stream { get; init; }

    public void Dispose() => Stream.Dispose();
}

internal sealed class NamedPipeListenerState : IAsyncDisposable
{
    public ValueTask DisposeAsync() => default;
}
