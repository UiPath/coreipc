using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
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
                Console.WriteLine($"Service is exposed over pipe \"{pipeName}\"");
                Console.WriteLine("Press CTRL_Q to terminate...");

                // Press any key to terminate...");
                ConsoleKeyInfo keyInfo;
                do
                {
                    keyInfo = Console.ReadKey(true);
                } while ((keyInfo.Modifiers & ConsoleModifiers.Control) == 0 || keyInfo.Key != ConsoleKey.Q);
            }
        }

        private sealed class Service : Contract.IService
        {
            public async Task<Contract.Complex> AddAsync(Contract.Complex a, Message<Contract.Complex> b, CancellationToken ct = default)
            {
                var callback = b.Client.GetCallback<Contract.ICallback>();

                //var stopwatch = new Stopwatch();
                //stopwatch.Start();

                //int i = 0;
                //while (stopwatch.Elapsed < TimeSpan.FromMinutes(0.5))
                //{
                //    await callback.AddAsync(10, i++);
                //    await Task.Delay(300);
                //}

                var x = await callback.AddAsync(a.X, b.Payload.X);
                ct.ThrowIfCancellationRequested();

                var y = await callback.AddAsync(a.Y, b.Payload.Y);
                ct.ThrowIfCancellationRequested();

                return new Contract.Complex { X = x, Y = y };
            }

            public async Task StartTimerAsync(Message message)
            {
                var callback = message.Client.GetCallback<Contract.ICallback>();

                while (true)
                {
                    try
                    {
                        await callback.TimeAsync(DateTime.Now.ToLongTimeString());
                    }
                    catch
                    {
                        return;
                    }
                    await Task.Delay(1000);
                }
            }
        }

        public static IServiceProvider ConfigureServices() => new ServiceCollection()
            .AddLogging(b => b.AddTraceSource(new SourceSwitch("", "All")))
            .AddIpc()
            .AddSingleton<Contract.IService, Service>()
            .BuildServiceProvider();
    }
}
