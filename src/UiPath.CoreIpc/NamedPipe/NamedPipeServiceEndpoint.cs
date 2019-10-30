using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.NamedPipe
{
    public class NamedPipeServiceEndpoint<TContract> : ServiceEndpoint where TContract : class
    {
        public NamedPipeServiceEndpoint(IServiceProvider serviceProvider, NamedPipeEndpointSettings<TContract> namedPipeEndpointSettings) : 
            base(serviceProvider, namedPipeEndpointSettings, serviceProvider.GetRequiredService<ILogger<NamedPipeServiceEndpoint<TContract>>>())
        {
        }

        public new NamedPipeEndpointSettings<TContract> Settings => (NamedPipeEndpointSettings<TContract>)base.Settings;

        protected override async Task AcceptConnection(CancellationToken token)
        {
            var server = new NamedPipeServerStream(Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous
#if NET461
                , inBufferSize: 0, outBufferSize: 0, GetPipeSecurity()
#endif
                );
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
                    Logger.LogException(ex, Name);
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
}