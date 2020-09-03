using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using System;
using System.Threading;
using System.Threading.Tasks;
using UiPath.CoreIpc.NamedPipe;

namespace UiPath.CoreIpc.SampleServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Debugger.Launch();
            try
            {
                await MainCore(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        static async Task MainCore(string[] args)
        {
            Console.WriteLine("Server process started...");

            const string mutexName = "68b514a2-d726-4577-9271-fef98fc10f36";
            using var _ = new Mutex(initiallyOwned: false, mutexName, out bool createdNew);
            if (!createdNew)
            {
                Console.WriteLine("Server already exists. Will now terminate...");
                return;
            }
            Console.WriteLine("Powering on...");

            if (args.Length == 0)
            {
                throw new Exception("Expecting a pipe name as the 1st command line argument.");
            }

            string pipeName = args[0];

            var services = new ServiceCollection();

            var sp = services
                .AddLogging()
                .AddIpc()
                .AddSingleton<IArithmetics, Arithmetics>()
                .BuildServiceProvider();

            var serviceHost = new ServiceHostBuilder(sp)
                .UseNamedPipes(new NamedPipeSettings(pipeName))
                .AddEndpoint<IArithmetics>()
                .Build();

            var thread = new AsyncContextThread();
            thread.Context.SynchronizationContext.Send(_ => Thread.CurrentThread.Name = "GuiThread", null);
            await serviceHost.RunAsync(thread.Context.Scheduler);
        }
    }
}
