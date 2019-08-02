using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UiPath.Ipc;
using UiPath.Ipc.NamedPipe;
using UiPath.Ipc.Tests;

namespace IpcSampleServerForNodejs
{
    public interface IChatService
    {
        Task HeaderAsync(string title);
        Task<object> Fail1Async();
        Task<object> Fail2Async();
        Task<object> Fail3Async();
        Task<int> SendAsync(int id, Message<string> message);

        Task<int> SumAsync(int x, int y);
        Task<int> MultiplyAsync(int x, int y);
    }
    public interface IChatCallback
    {
        Task<int> ReceiveAsync(int id, Message<string> text);
    }

    public class ChatService : IChatService
    {
        #pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<object> Fail1Async()
        {
            using (Program.MarkAsUsed())
            {
                throw new Exception("Foo");
            }
        }
        #pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

        public Task<object> Fail2Async()
        {
            using (Program.MarkAsUsed())
            {
                throw new Exception("Foo");
            }
        }
        public Task<object> Fail3Async()
        {
            using (Program.MarkAsUsed())
            {
                return Task.FromException<object>(new Exception("Foo"));
            }
        }



        public Task HeaderAsync(string title)
        {
            using (Program.MarkAsUsed())
            {
                const string stars = "************************************************************************************************************************************";

                int left = (stars.Length - title.Length) / 2;
                int right = stars.Length - left - title.Length;

                Console.WriteLine(stars);
                Console.WriteLine(title.PadLeft(left, ' ').PadRight(right, ' '));
                Console.WriteLine(stars);
                Console.WriteLine();

                return Task.CompletedTask;
            }
        }
        public async Task<int> SendAsync(int id, Message<string> message)
        {
            using (Program.MarkAsUsed())
            {
                var callback = message.Client.GetCallback<IChatCallback>();
                var temp = await callback.ReceiveAsync(id, new Message<string>(message.Payload)
                {
                    RequestTimeout = TimeSpan.FromMinutes(10)
                });
                return temp;
            }
        }

        public async Task<int> SumAsync(int x, int y)
        {
            using (Program.MarkAsUsed())
            {
                return x + y;
            }
        }
        public async Task<int> MultiplyAsync(int x, int y)
        {
            using (Program.MarkAsUsed())
            {
                return x * y;
            }
        }
    }

    class Program
    {
        private static int s_connectionCount;
        private static readonly object s_lock = new object();
        private static readonly Stopwatch s_stopwatch = new Stopwatch();

        static Program()
        {
            s_stopwatch.Start();
        }

        public static IDisposable MarkAsUsed()
        {
            Interlocked.Increment(ref s_connectionCount);
            return Dec.Instance;
        }
        private sealed class Dec : IDisposable
        {
            public static IDisposable Instance { get; } = new Dec();
            public void Dispose()
            {
                if (0 == Interlocked.Decrement(ref s_connectionCount))
                {
                    lock (s_lock)
                    {
                        s_stopwatch.Restart();
                    }
                }
            }
        }


        static void Main(string[] args)
        {
            Task.Run(async () =>
            {

                while (true)
                {
                    await Task.Delay(1000);
                    lock (s_lock)
                    {
                        if (s_stopwatch.IsRunning && s_stopwatch.Elapsed > TimeSpan.FromSeconds(5))
                        {
                            Process.GetCurrentProcess().Kill();
                        }
                    }
                }

            });

            string pipeName = args[0];

            var serviceProvider = ConfigureServices();
            var _host = new ServiceHostBuilder(serviceProvider)
                .AddEndpoint(new NamedPipeEndpointSettings<IChatService, IChatCallback>(pipeName)
                {
                    RequestTimeout = TimeSpan.FromSeconds(10),
                    AccessControl = security => { },
                })
                .Build();
            using (GuiLikeSyncContext.Install())
            {
                _host.RunAsync(TaskScheduler.FromCurrentSynchronizationContext());
            }

            Console.WriteLine("Server is running...");
            new ManualResetEvent(false).WaitOne();
        }

        public static IServiceProvider ConfigureServices() => new ServiceCollection()
            .AddLogging()
            .AddIpc()
            .AddSingleton<IChatService, ChatService>()
            .BuildServiceProvider();
    }
}
