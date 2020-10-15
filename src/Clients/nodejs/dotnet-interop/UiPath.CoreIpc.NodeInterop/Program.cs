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
        /// <summary>
        /// .NET - Nodejs Interop Helper
        /// </summary>
        /// <param name="pipe">The pipe name on which the CoreIpc endpoints will be hosted at.</param>
        /// <param name="mutex">Optional process mutual exclusion name.</param>
        /// <param name="delay">Optional number of seconds that the process will wait before it exposes the CoreIpc endpoints.</param>
        static async Task<int> Main(
            string pipe,
            string? mutex = null,
            int? delay = null)
        {
            if (pipe is null)
            {
                Console.Error.WriteLine($"Expecting a non-null pipe-name.");
                return 1;
            }

            try
            {
                if (mutex is { })
                {
                    using var _ = new Mutex(initiallyOwned: false, mutex, out bool createdNew);
                    if (!createdNew) { return 2; }
                    await MainCore(pipe, delay);
                }
                else
                {
                    await MainCore(pipe, delay);
                }
            }
            catch (Exception ex)
            {
                Throw(ex);
                throw;
            }

            return 0;
        }

        static async Task MainCore(string pipeName, int? maybeSecondsPowerOnDelay)
        {
            if (maybeSecondsPowerOnDelay is { } secondsPowerOnDelay)
            {
                await Task.Delay(TimeSpan.FromSeconds(secondsPowerOnDelay));
            }

            Send(SignalKind.PoweringOn);
            var services = new ServiceCollection();

            var sp = services
                .AddLogging()
                .AddIpc()
                .AddSingleton<IAlgebra, Algebra>()
                .AddSingleton<ICalculus, Calculus>()
                .AddSingleton<IBrittleService, BrittleService>()
                .AddSingleton<IEnvironmentVariableGetter, EnvironmentVariableGetter>()
                .BuildServiceProvider();

            var serviceHost = new ServiceHostBuilder(sp)
                .UseNamedPipes(new NamedPipeSettings(pipeName))
                .AddEndpoint<IAlgebra, IArithmetics>()
                .AddEndpoint<ICalculus>()
                .AddEndpoint<IBrittleService>()
                .AddEndpoint<IEnvironmentVariableGetter>()
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
