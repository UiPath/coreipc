using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using UiPath.CoreIpc.Tcp;

namespace UiPath.CoreIpc.Tests
{
    class TcpServer
    {
        static readonly IPEndPoint SystemEndPoint = new(IPAddress.Any, 3131);
        //private static readonly Timer _timer = new Timer(_ =>
        //{
        //    Console.WriteLine("GC.Collect");
        //    GC.Collect();
        //    GC.WaitForPendingFinalizers();
        //    GC.Collect();
        //}, null, 0, 3000);

        static async Task Main()
        {
            Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
            //GuiLikeSyncContext.Install();
            Console.WriteLine(SynchronizationContext.Current);
            var serviceProvider = ConfigureServices();
            // build and run service host
            var data = File.ReadAllBytes(@"../../../../localhost.pfx");
            var host = new ServiceHostBuilder(serviceProvider)
                .UseTcp(new TcpSettings(SystemEndPoint)
                {
                    RequestTimeout = TimeSpan.FromSeconds(2),
                    Certificate = new X509Certificate(data, "1"),
                })
                .AddEndpoint<IComputingService, IComputingCallback>()
                .AddEndpoint<ISystemService>()
                .ValidateAndBuild();

            await await Task.WhenAny(host.RunAsync(), Task.Run(() =>
            {
                Console.WriteLine(typeof(int).Assembly);
                Console.ReadLine();
                host.Dispose();
            }));

            Console.WriteLine("Server stopped.");
        }

        private static IServiceProvider ConfigureServices() =>
            new ServiceCollection()
                .AddIpcWithLogging()
                .AddSingleton<IComputingService, ComputingService>()
                .AddSingleton<ISystemService, SystemService>()
                .BuildServiceProvider();
    }
}