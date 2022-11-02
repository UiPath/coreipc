using System.Text;
using System.Diagnostics;
using UiPath.CoreIpc.NamedPipe;
using Microsoft.Extensions.DependencyInjection;

namespace UiPath.CoreIpc.Tests;

class Client
{
    static async Task Main(string[] args)
    {
        Console.WriteLine(typeof(int).Assembly);
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
        var source = new CancellationTokenSource();
        try
        {
            await await Task.WhenAny(RunTestsAsync(source.Token), Task.Run(() =>
            {
                Console.ReadLine();
                Console.WriteLine("Cancelling...");
                source.Cancel();
            }));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
        Console.ReadLine();
    }
    private static async Task RunTestsAsync(CancellationToken cancellationToken)
    {
        var serviceProvider = ConfigureServices();

        var systemClient =
            new NamedPipeClientBuilder<ISystemService>("test")
            .SerializeParametersAsObjects()
            .RequestTimeout(TimeSpan.FromSeconds(12))
            .Logger(serviceProvider)
            .AllowImpersonation()
            .ValidateAndBuild();
        while (true)
        {
            var tasks = Enumerable.Repeat(1, 1).Select(_ => Task.Run(async() =>
            {
                var watch = Stopwatch.StartNew();
                JobResult jobResult;
                for (int i = 0; i < 30; i++)
                {
                    jobResult = await systemClient.GetJobResult();
                }
                watch.Stop();
                var gcStats = GC.GetGCMemoryInfo();
                Console.WriteLine($"{watch.ElapsedMilliseconds} {gcStats.GenerationInfo[2].SizeAfterBytes/1_000_000}  {gcStats.PauseTimePercentage}");
            }));
            await Task.WhenAll(tasks);
        }
    }

    private static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddIpcWithLogging()
            .BuildServiceProvider();
}