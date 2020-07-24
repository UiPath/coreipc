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

// Debugger.Launch();

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
    Task<bool> Sleep(int milliseconds, Message message = default, CancellationToken ct = default);
    Task<bool> Timeout();
    Task<int> Echo(int x);
}

public interface ICalculus
{
    Task<string> Ping();
}

public sealed class Algebra : IAlgebra
{
    public async Task<string> Ping()
    {
        return "Pong";
    }

    public Task<int> MultiplySimple(int x, int y)
    {
        Console.WriteLine($"{nameof(MultiplySimple)}({x}, {y})");
        return Task.FromResult(x * y);
    }

    public async Task<int> Multiply(int x, int y, Message message = default)
    {
        var arithmetics = message.GetCallback<IArithmetics>();

        int result = 0;
        for (int i = 0; i < x; i++)
        {
            result = await arithmetics.Sum(result, y);
        }

        return result;
    }

    public async Task<bool> Sleep(int milliseconds, Message message = default, CancellationToken ct = default)
    {
        throw new System.TimeoutException();

        await Task.Delay(milliseconds, ct);
        return true;
    }

    public async Task<bool> Timeout()
    {
        throw new TimeoutException();
    }

    public async Task<int> Echo(int x)
    {
        return x;
    }
}

public class Calculus : ICalculus
{
    public async Task<string> Ping()
    {
        return "Pong";
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
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.GetType().Name}\\r\\nMessage: {ex.Message}\\r\\nStack: {ex.StackTrace}");
    }
}
