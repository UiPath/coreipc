using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.NamedPipe
{
    public class NamedPipeSettings : ListenerSettings
    {
        public NamedPipeSettings(string pipeName) : base(pipeName) { }
        public Action<PipeSecurity> AccessControl { get; set; }
    }
    class NamedPipeListener : Listener
    {
        public NamedPipeListener(NamedPipeSettings settings) : base(settings) { }
        protected override ServerConnection CreateServerConnection() => new NamedPipeServerConnection(this);
        class NamedPipeServerConnection : ServerConnection
        {
            readonly NamedPipeServerStream _server;
            public NamedPipeServerConnection(Listener listener) : base(listener)
            {
                _server = IOHelpers.NewNamedPipeServerStream(Settings.Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, GetPipeSecurity);
            }
            public override async Task AcceptClient(CancellationToken cancellationToken)
            {
                // on linux WaitForConnectionAsync has to be cancelled with Dispose
                using (cancellationToken.Register(Dispose))
                {
                    await _server.WaitForConnectionAsync();
                }
            }
            protected override Stream Network => _server;
            public override void Impersonate(Action action) => _server.RunAsClient(()=>action());
            protected override void Dispose(bool disposing)
            {
                _server.Dispose();
                base.Dispose(disposing);
            }
            PipeSecurity GetPipeSecurity()
            {
                var setAccessControl = ((NamedPipeSettings)Settings).AccessControl;
                if (setAccessControl == null)
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
    public static class NamedPipeServiceExtensions
    {
        public static ServiceHostBuilder UseNamedPipes(this ServiceHostBuilder builder, NamedPipeSettings settings) => 
            builder.AddListener(new NamedPipeListener(settings));
    }
}