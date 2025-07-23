using Newtonsoft.Json;
using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.Transport.NamedPipe;

public sealed class NamedPipeServerTransport : ServerTransport
{
    public required string PipeName { get; init; }
    public string ServerName { get; init; } = ".";
    [JsonIgnore]
    public AccessControlDelegate? AccessControl { get; init; }

    internal override IServerState CreateServerState()
    => new ServerState { Transport = this };

    internal override IEnumerable<string?> ValidateCore()
    {
        yield return IsNotNull(PipeName);
        yield return IsNotNull(ServerName);
    }

    public override string ToString() => $"ServerPipe={PipeName}";

    private sealed class ServerState : IServerState
    {
        public required NamedPipeServerTransport Transport { get; init; }

        IServerConnectionSlot IServerState.CreateConnectionSlot() => ServerConnectionState.Create(serverState: this);

        ValueTask IAsyncDisposable.DisposeAsync() => default;
    }

    private sealed class ServerConnectionState : IServerConnectionSlot
    {
        public static ServerConnectionState Create(ServerState serverState)
        {
            return new()
            {
                Stream = CreateStream()
            };

            NamedPipeServerStream CreateStream()
            => IOHelpers.NewNamedPipeServerStream(
                serverState.Transport.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                GetPipeSecurity);

            PipeSecurity? GetPipeSecurity()
            {
                if (serverState.Transport.AccessControl is not { } setAccessControl)
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
        }

        public required NamedPipeServerStream Stream { get; init; }

        ValueTask IAsyncDisposable.DisposeAsync()
        {
#if NET461
            Stream.Dispose();
            return default;
#else
            return Stream.DisposeAsync();
#endif
        }

        async ValueTask<Stream> IServerConnectionSlot.AwaitConnection(CancellationToken ct)
        {
            // on Linux WaitForConnectionAsync has to be cancelled with Dispose
            using (ct.Register(StartDisposal))
            {
                await Stream.WaitForConnectionAsync(ct);
                return Stream;
            }

            void StartDisposal() => (this as IAsyncDisposable).DisposeAsync().AsTask().TraceError(); // We trace the error even we don't expect Dispose/DisposeAsync to ever throw.
        }
    }
}
