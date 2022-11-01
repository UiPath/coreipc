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
            Console.ReadLine();
        }
    }
    private static async Task RunTestsAsync(CancellationToken cancellationToken)
    {
        var serviceProvider = ConfigureServices();

        var systemClient =
            new NamedPipeClientBuilder<ISystemService>("test")
            .SerializeParametersAsObjects()
            .RequestTimeout(TimeSpan.FromSeconds(2))
            .Logger(serviceProvider)
            .AllowImpersonation()
            .ValidateAndBuild();

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 30; i++)
        {
            await systemClient.GetJobResult();
        }

        Console.WriteLine($"Elapsed {watch.ElapsedMilliseconds}");
    }

    private static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddIpcWithLogging()
            .BuildServiceProvider();
}