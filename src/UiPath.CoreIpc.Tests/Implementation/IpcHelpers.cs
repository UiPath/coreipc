using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;

namespace UiPath.CoreIpc
{
    public static class IpcHelpers
    {
        public static string CurrentUserName()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                return identity.Name;
            }
        }
        public static string GetUserName(this IClient client)
        {
            string userName = null;
            client.Impersonate(() => userName = CurrentUserName());
            return userName;
        }
        public static IServiceCollection AddIpcWithLogging(this IServiceCollection services, bool logToConsole = false)
        {
            services.AddLogging(builder =>
            {
                if (logToConsole)
                {
                    builder.AddConsole();
                }
                foreach(var listener in Trace.Listeners.Cast<TraceListener>().Where(l => !(l is DefaultTraceListener)))
                {
                    builder.AddTraceSource(new SourceSwitch(listener.Name, "All"), listener);
                }
            });
            return services.AddIpc();
        }
    }
}