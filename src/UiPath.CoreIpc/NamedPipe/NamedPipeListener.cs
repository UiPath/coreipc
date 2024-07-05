using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.NamedPipe;

internal sealed class NamedPipeListener : Listener
{
    public new NamedPipeListenerConfig Config { get; }

    public NamedPipeListener(IpcServer server, NamedPipeListenerConfig config) : base(server, config)
    {
        Config = config;

        EnsureListening();
    }

    protected override ServerConnection CreateServerConnection() => new NamedPipeServerConnection(this);

    private sealed class NamedPipeServerConnection : ServerConnection
    {
        private readonly NamedPipeServerStream _server;
        private new readonly NamedPipeListener _listener;

        public NamedPipeServerConnection(NamedPipeListener listener) : base(listener)
        {
            _listener = listener;
            _server = IOHelpers.NewNamedPipeServerStream(listener.Config.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, GetPipeSecurity);
        }
        public override async Task<Stream> AcceptClient(CancellationToken cancellationToken)
        {
            await _server.WaitForConnectionAsync(cancellationToken);
            return _server;
        }
        public override void Impersonate(Action action) => _server.RunAsClient(() => action());
        protected override void Dispose(bool disposing)
        {
            _server.Dispose();
            base.Dispose(disposing);
        }
        PipeSecurity? GetPipeSecurity()
        {
            var setAccessControl = _listener.Config.AccessControl;
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
    }
}
