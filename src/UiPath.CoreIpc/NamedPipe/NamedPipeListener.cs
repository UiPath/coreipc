using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.NamedPipe;

public sealed class NamedPipeListener : Listener<NamedPipeListenerConfig, NamedPipeListener.NamedPipeServerConnection>
{
    public sealed class NamedPipeServerConnection : ServerConnection<NamedPipeListener>
    {
        private NamedPipeServerStream _server = null!;

        protected internal override void Initialize()
        {
            _server = IOHelpers.NewNamedPipeServerStream(Listener.Config.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
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
            var setAccessControl = Listener.Config.AccessControl;
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
