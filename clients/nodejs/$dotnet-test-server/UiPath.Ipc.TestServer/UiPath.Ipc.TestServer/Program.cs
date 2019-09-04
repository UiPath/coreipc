using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using UiPath.Ipc.NamedPipe;

namespace UiPath.Ipc.TestServer
{
    class Program
    {
        static void Main(string[] args)
        {
            const string pipeName = "foo-pipe";
            var _serviceProvider = ConfigureServices();
            PipeSecurity _pipeSecurity;
            var _host = new ServiceHostBuilder(_serviceProvider)
                .AddEndpoint(new NamedPipeEndpointSettings<Contract.IService, Contract.ICallback>(pipeName)
                {
                    RequestTimeout = TimeSpan.FromSeconds(1),
                    AccessControl = security => _pipeSecurity = security,
                })
                .Build();

            using (GuiLikeSyncContext.Install())
            {
                _host.RunAsync(TaskScheduler.FromCurrentSynchronizationContext());
                Console.WriteLine($"Server is running (pipeName == \"{pipeName}\"). Press any key to terminate...");
                Console.ReadKey(true);
            }
        }

        private sealed class Service : Contract.IService
        {
            public async Task<Contract.Complex> AddAsync(Contract.Complex a, Message<Contract.Complex> b, CancellationToken ct = default)
            {
                var callback = b.Client.GetCallback<Contract.ICallback>();
                var x = await callback.AddAsync(a.X, b.Payload.X);
                ct.ThrowIfCancellationRequested();

                var y = await callback.AddAsync(a.Y, b.Payload.Y);
                ct.ThrowIfCancellationRequested();

                return new Contract.Complex { X = x, Y = y };
            }
        }

        public static IServiceProvider ConfigureServices() => new ServiceCollection()
            .AddLogging(b => b.AddTraceSource(new SourceSwitch("", "All")))
            .AddIpc()
            .AddSingleton<Contract.IService, Service>()
            .BuildServiceProvider();
    }
}
