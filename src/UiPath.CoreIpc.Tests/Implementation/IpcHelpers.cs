using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using UiPath.CoreIpc.Tests;

namespace UiPath.CoreIpc
{
    public static class IpcHelpers
    {
        public static TInterface ValidateAndBuild<TDerived, TInterface>(this ServiceClientBuilder<TDerived, TInterface> builder) where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
        {
#if DEBUG
            Validator.Validate(builder);
#endif
            return builder.Build();
        }
        public static ServiceHost ValidateAndBuild(this ServiceHostBuilder serviceHostBuilder)
        {
#if DEBUG
            Validator.Validate(serviceHostBuilder);
#endif
            return serviceHostBuilder.Build();
        }
        public static IServiceProvider ConfigureServices() =>
            new ServiceCollection()
                .AddLogging(b => b.AddTraceSource(new SourceSwitch("", "All")))
                .AddIpc()
                .AddSingleton<IComputingServiceBase, ComputingService>()
                .AddSingleton<IComputingService, ComputingService>()
                .AddSingleton<ISystemService, SystemService>()
                .BuildServiceProvider();
        public static string GetUserName(this IClient client)
        {
            string userName = null;
            client.Impersonate(() => userName = Environment.UserName);
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