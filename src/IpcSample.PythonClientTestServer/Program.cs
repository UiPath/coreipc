// Test-only IPC server purpose-built for the Python client's integration suite.
//
// Differences from IpcSample.ConsoleServer:
//   - Console logging is enabled (visible in pytest output).
//   - Stable READY marker for the Python fixture.
//   - Most handlers are callback-free, so the basic test suite works
//     against a callback-less Python client. ICallbackTester is the
//     exception — it deliberately exercises the server-to-client
//     callback path the Python uipath-ipc client added in 0.2.0.
//   - Pipe name configurable via the first CLI argument; defaults to
//     "uipath-ipc-py-test".

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UiPath.Ipc;
using UiPath.Ipc.Transport.NamedPipe;

namespace IpcSample.PythonClientTestServer;

public interface IComputingService
{
    Task<float> AddFloats(float x, float y, CancellationToken ct = default);
    Task<int> MultiplyInts(int x, int y, CancellationToken ct = default);
    Task<ComplexNumber> AddComplexNumbers(ComplexNumber a, ComplexNumber b, CancellationToken ct = default);
    Task<bool> DivideByZero(CancellationToken ct = default);
    Task<bool> Wait(TimeSpan duration, CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="Wait"/>, but with a trailing <see cref="Message"/> so a
    /// client can attach a per-call timeout (Message.RequestTimeout rides the
    /// Request envelope as TimeoutInSeconds, overriding the server default).
    /// </summary>
    Task<bool> WaitWithMessage(TimeSpan duration, Message m = null!, CancellationToken ct = default);
}

public interface ISystemService
{
    Task<string> EchoString(string value, CancellationToken ct = default);
    Task<byte[]> ReverseBytes(byte[] data, CancellationToken ct = default);

    // Value types JSON has no native form for — Newtonsoft sends byte[] as
    // base64, Guid/DateTime as strings — so the Python client must encode/
    // decode them by the declared type to round-trip correctly.
    Task<Guid> EchoGuid(Guid value, CancellationToken ct = default);
    Task<DateTime> EchoDateTime(DateTime value, CancellationToken ct = default);
    Task<Person> EchoPerson(Person value, CancellationToken ct = default);
}

public sealed record Person
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public override string ToString() => $"{FirstName} {LastName}";
}

/// <summary>
/// Contract for a callback the *client* hosts and the *server* invokes.
/// Used by ICallbackTester below to exercise the bidirectional path.
/// Note: callback interfaces don't declare CancellationToken parameters
/// (matching the .NET test suite's IComputingCallback convention) — the
/// server-side caller doesn't include CT in the wire Parameters array.
/// </summary>
public interface IClientCallback
{
    Task<string> EchoToClient(string value);
    Task<int> AddOnClient(int x, int y);
}

/// <summary>
/// Service the client calls into; each method then calls *back* into
/// the client's IClientCallback. Lets us verify the server→client
/// callback path end-to-end from a Python integration test.
/// </summary>
public interface ICallbackTester
{
    Task<string> TriggerEcho(string value, Message message = null!, CancellationToken ct = default);
    Task<int> TriggerAdd(int x, int y, Message message = null!, CancellationToken ct = default);
}

public readonly record struct ComplexNumber
{
    public required float I { get; init; }
    public required float J { get; init; }
    public override string ToString() => $"[{I}, {J}]";
}

public sealed class ComputingService : IComputingService
{
    private readonly ILogger<ComputingService> _logger;
    public ComputingService(ILogger<ComputingService> logger) => _logger = logger;

    public Task<float> AddFloats(float x, float y, CancellationToken ct)
    {
        _logger.LogInformation("AddFloats({X}, {Y})", x, y);
        return Task.FromResult(x + y);
    }

    public Task<int> MultiplyInts(int x, int y, CancellationToken ct)
    {
        _logger.LogInformation("MultiplyInts({X}, {Y})", x, y);
        return Task.FromResult(x * y);
    }

    public Task<ComplexNumber> AddComplexNumbers(ComplexNumber a, ComplexNumber b, CancellationToken ct)
    {
        _logger.LogInformation("AddComplexNumbers({A}, {B})", a, b);
        return Task.FromResult(new ComplexNumber { I = a.I + b.I, J = a.J + b.J });
    }

    public Task<bool> DivideByZero(CancellationToken ct)
    {
        _logger.LogInformation("DivideByZero (about to throw)");
        throw new DivideByZeroException("intentional");
    }

    public async Task<bool> Wait(TimeSpan duration, CancellationToken ct)
    {
        _logger.LogInformation("Wait({Duration})", duration);
        await Task.Delay(duration, ct);
        return true;
    }

    public async Task<bool> WaitWithMessage(TimeSpan duration, Message m, CancellationToken ct)
    {
        _logger.LogInformation("WaitWithMessage({Duration})", duration);
        await Task.Delay(duration, ct);
        return true;
    }
}

public sealed class SystemService : ISystemService
{
    private readonly ILogger<SystemService> _logger;
    public SystemService(ILogger<SystemService> logger) => _logger = logger;

    public Task<string> EchoString(string value, CancellationToken ct)
    {
        _logger.LogInformation("EchoString({Value})", value);
        return Task.FromResult(value);
    }

    public Task<Guid> EchoGuid(Guid value, CancellationToken ct) => Task.FromResult(value);

    public Task<DateTime> EchoDateTime(DateTime value, CancellationToken ct) => Task.FromResult(value);

    public Task<Person> EchoPerson(Person value, CancellationToken ct) => Task.FromResult(value);

    public Task<byte[]> ReverseBytes(byte[] data, CancellationToken ct)
    {
        _logger.LogInformation("ReverseBytes(len={Length})", data.Length);
        var copy = (byte[])data.Clone();
        Array.Reverse(copy);
        return Task.FromResult(copy);
    }
}

public sealed class CallbackTester : ICallbackTester
{
    private readonly ILogger<CallbackTester> _logger;
    public CallbackTester(ILogger<CallbackTester> logger) => _logger = logger;

    public async Task<string> TriggerEcho(string value, Message m, CancellationToken ct)
    {
        _logger.LogInformation("TriggerEcho({Value}) → calling client back", value);
        var cb = m.Client.GetCallback<IClientCallback>();
        return await cb.EchoToClient(value);
    }

    public async Task<int> TriggerAdd(int x, int y, Message m, CancellationToken ct)
    {
        _logger.LogInformation("TriggerAdd({X}, {Y}) → calling client back", x, y);
        var cb = m.Client.GetCallback<IClientCallback>();
        return await cb.AddOnClient(x, y);
    }
}

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var pipeName = args.Length > 0 ? args[0] : "uipath-ipc-py-test";

        await using var serviceProvider = new ServiceCollection()
            .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information))
            .AddSingleton<IComputingService, ComputingService>()
            .AddSingleton<ISystemService, SystemService>()
            .AddSingleton<ICallbackTester, CallbackTester>()
            .BuildServiceProvider();

        await using var server = new IpcServer
        {
            Transport = new NamedPipeServerTransport { PipeName = pipeName },
            ServiceProvider = serviceProvider,
            Endpoints = new()
            {
                typeof(IComputingService),
                typeof(ISystemService),
                typeof(ICallbackTester),
            },
            RequestTimeout = TimeSpan.FromSeconds(2),
        };

        server.Start();
        // IpcServer.Start() is fire-and-forget; the pipe accepter spins up
        // shortly after. The Python client's connect retry rides out the
        // brief window before the first pipe instance is ready.
        Console.WriteLine($"READY pipe={pipeName}");

        var tcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult(null);
        };
        await tcs.Task;

        Console.WriteLine("STOPPED");
    }
}
