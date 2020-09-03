using System;
using System.IO;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.ResilientClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string pathServer = Path.Combine(
                Environment.CurrentDirectory,
                "..", "..", "..", "..",
                "UiPath.CoreIpc.BrittleServer",
                "bin",
                "Debug",
                "netcoreapp3.1",
                "UiPath.CoreIpc.BrittleServer.exe");

            string pipeName = Guid.NewGuid().ToString();

            var arithmetics = Helpers
                .CreateBuilder(pathServer, pipeName)
                .Build();

            var utcNow = DateTime.UtcNow;
            int result;

            while (true)
            {
                try
                {
                    result = await arithmetics.Sum(10, 20, TimeSpan.FromSeconds(1), failBeforeUtc: utcNow + TimeSpan.FromSeconds(10));
                    break;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"{ex.GetType().Name}: \"{ex.Message}\"");
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(result);
        }
    }
}

