using System;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UiPath.CoreIpc.NamedPipe;
using UiPath.CoreIpc.SampleServer;

namespace UiPath.CoreIpc.ResilientClient
{
    static class Helpers
    {
        public static bool PipeExists(string pipeName)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new NotImplementedException();
            }
            return WaitNamedPipe(@"\\.\pipe\" + pipeName, timeoutMilliseconds: 1); // 0 is NMPWAIT_USE_DEFAULT_WAIT
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern bool WaitNamedPipe(string pipeName, int timeoutMilliseconds);

        public static NamedPipeClientBuilder<IArithmetics> CreateBuilder(string pathServer, string pipeName)
        {
            return new NamedPipeClientBuilder<IArithmetics>(pipeName)
                .RequestTimeout(TimeSpan.FromSeconds(20))
                .ConnectionFactory(EnsureServerIsRunning);

            Task<Connection> EnsureServerIsRunning(Connection connection, CancellationToken ct)
            {
                if (!PipeExists(pipeName))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Pipe not found. Starting server");

                    _ = RunAsync();

                    async Task RunAsync()
                    {
                        using var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = pathServer,
                            Arguments = pipeName,
                            RedirectStandardOutput = true,
                        });

                        while (true)
                        {
                            try
                            {
                                string line = await process.StandardOutput.ReadLineAsync();
                                if (line is null)
                                {
                                    await Task.Delay(10);
                                    continue;
                                }
                                Console.ForegroundColor = ConsoleColor.Magenta;
                                Console.WriteLine($"Server (PID={process.Id}): {line}");
                            }
                            catch
                            {
                                break;
                            }
                        }
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Server (PID={process.Id}): The process has exited.");
                    }
                }
                return Task.FromResult(default(Connection));
            }
        }

        public static Task WaitForExitAsync(this Process process)
        {
            if (process.HasExited) { return Task.CompletedTask; }

            var tcs = new TaskCompletionSource<object>();
            process.Exited += (_, __) => tcs.TrySetResult(null);
            return tcs.Task;
        }
    }
}
