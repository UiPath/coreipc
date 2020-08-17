using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UiPath.CoreIpc.NamedPipe;

namespace UiPath.CoreIpc.NodeInterop
{
    using static ServiceImpls;
    using static Contracts;
    using static Signalling;

    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                await MainCore(args);
            }
            catch (Exception ex)
            {
                Throw(ex);
                throw;
            }
        }

        static async Task MainCore(string[] args)
        {
            if (args.Length == 0)
            {
                throw new Exception("Expecting a pipe name as the 1st command line argument.");
            }

            string pipeName = args[0];

            Send(SignalKind.PoweringOn);
            var services = new ServiceCollection();

            var sp = services
                .AddLogging()
                .AddIpc()
                .AddSingleton<IAlgebra, Algebra>()
                .AddSingleton<ICalculus, Calculus>()
                .BuildServiceProvider();

            var serviceHost = new ServiceHostBuilder(sp)
                .UseNamedPipes(new NamedPipeSettings(pipeName))
                .AddEndpoint<IAlgebra, IArithmetics>()
                .AddEndpoint<ICalculus>()
                .Build();

            var thread = new AsyncContextThread();
            thread.Context.SynchronizationContext.Send(_ => Thread.CurrentThread.Name = "GuiThread", null);
            var sched = thread.Context.Scheduler;

            _ = Task.Run(async () =>
            {
                try
                {
                    await new NamedPipeClientBuilder<IAlgebra>(pipeName)
                        .RequestTimeout(TimeSpan.FromSeconds(2))
                        .Build()
                        .Ping();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                Send(SignalKind.ReadyToConnect);
            });

            await serviceHost.RunAsync(sched);
        }

    }
}
