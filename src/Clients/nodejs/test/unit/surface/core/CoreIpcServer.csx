#r "nuget: Microsoft.Extensions.DependencyInjection.Abstractions, 3.0.0"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 3.0.0"

#r "nuget: Microsoft.Extensions.DependencyInjection, 3.0.0"
#r "nuget: Microsoft.Extensions.Logging, 3.0.0"

#r "nuget: Nito.AsyncEx.Context, 5.0.0"
#r "nuget: Nito.AsyncEx.Tasks, 5.0.0"
#r "nuget: Nito.AsyncEx.Coordination, 5.0.0"

#r "nuget: Newtonsoft.Json, 12.0.3"

#r "UiPath.CoreIpc.dll"

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using UiPath.CoreIpc;
using UiPath.CoreIpc.NamedPipe;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

#region " Signalling "

[JsonConverter(typeof(StringEnumConverter))]
enum SignalKind
{
    Throw,
    PoweringOn,
    ReadyToConnect,
}

class Signal
{
    public static implicit operator Signal(SignalKind signalKind) => new Signal { Kind = signalKind };
    public SignalKind Kind { get; set; }
}

class Signal<TDetails> : Signal
{
    public TDetails Details { get; set; }
}

Signal<TDetails> MakeSignal<TDetails>(SignalKind kind, TDetails details) => new Signal<TDetails>
{
    Kind = kind,
    Details = details
};

static void Send(Signal signal) => Console.WriteLine($"###{JsonConvert.SerializeObject(signal)}");

void Throw(Exception exception)
    => Send(MakeSignal(SignalKind.Throw, new
    {
        Type = exception.GetType().Name,
        Message = exception.Message
    }));


#endregion

public interface IArithmetics
{
    Task<int> Sum(int x, int y);
}

public interface IAlgebra
{
    Task<string> Ping();
    Task<int> MultiplySimple(int x, int y);
    Task<int> Multiply(int x, int y, Message message = default);
}

public sealed class Algebra : IAlgebra
{
    public Task<string> Ping() => Task.FromResult("Pong");

    public Task<int> MultiplySimple(int x, int y)
    {
        Console.WriteLine($"{nameof(MultiplySimple)}({x}, {y})");
        return Task.FromResult(x * y);
    }

    public async Task<int> Multiply(int x, int y, Message message = default)
    {
        // Debugger.Launch();
        var arithmetics = message.GetCallback<IArithmetics>();

        int result = 0;
        for (int i = 0; i < x; i++)
        {
            result = await arithmetics.Sum(result, y);
        }

        return result;
    }
}

await Main(Args);

async Task Main(IList<string> args)
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

static async Task MainCore(IList<string> args)
{
    if (args.Count == 0)
    {
        throw new Exception("Expecting a pipe name as the 1st command line argument.");
    }

    string pipeName = args[0];

    try
    {
        Send(SignalKind.PoweringOn);
        var services = new ServiceCollection();

        var sp = services
            .AddLogging()
            .AddIpc()
            .AddSingleton<IAlgebra, Algebra>()
            .BuildServiceProvider();

        var serviceHost = new ServiceHostBuilder(sp)
            .UseNamedPipes(new NamedPipeSettings(pipeName))
            .AddEndpoint<IAlgebra, IArithmetics>()
            .Build();

        var thread = new AsyncContextThread();
        thread.Context.SynchronizationContext.Send(_ => Thread.CurrentThread.Name = "GuiThread", null);
        var sched = thread.Context.Scheduler;

        _ = Task.Run(async () =>
        {
            await new NamedPipeClientBuilder<IAlgebra>(pipeName)
                .RequestTimeout(TimeSpan.FromSeconds(2))
                .Build()
                .Ping();
            Send(SignalKind.ReadyToConnect);
        });

        await serviceHost.RunAsync(sched);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.GetType().Name}\\r\\nMessage: {ex.Message}\\r\\nStack: {ex.StackTrace}");
    }
}
