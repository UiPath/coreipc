using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Linq;

namespace UiPath.Ipc
{
    public static class IpcHelpers
    {
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