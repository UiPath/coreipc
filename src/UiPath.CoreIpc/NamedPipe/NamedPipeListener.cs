using System;
using System.Collections.Generic;
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
        public new NamedPipeSettings Settings => (NamedPipeSettings)base.Settings;
        protected override async Task AcceptConnection(CancellationToken token)
        {
            var server = IOHelpers.NewNamedPipeServerStream(Settings.Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, GetPipeSecurity);
            try
            {
                // on linux WaitForConnectionAsync has to be cancelled with Dispose
                using (token.Register(server.Dispose))
                {
                    await server.WaitForConnectionAsync();
                }
                // pass the ownership of the connection
                HandleConnection(server, callbackFactory => new Client(action => server.RunAsClient(() => action()), callbackFactory), token);
            }
            catch (Exception ex)
            {
                server.Dispose();
                if (!token.IsCancellationRequested)
                {
                    Logger.LogException(ex, Settings.Name);
                }
            }
        }
        private PipeSecurity GetPipeSecurity()
        {
            var setAccessControl = Settings.AccessControl;
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
    public static class NamedPipeServiceExtensions
    {
        public static ServiceHostBuilder UseNamedPipes(this ServiceHostBuilder builder, NamedPipeSettings settings)
        {
            settings.ServiceProvider = builder.ServiceProvider;
            settings.Endpoints = builder.Endpoints;
            builder.AddListener(new NamedPipeListener(settings));
            return builder;
        }
    }
}