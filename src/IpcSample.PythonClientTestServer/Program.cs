// Test-only IPC server purpose-built for the Python client's integration suite.
//
// Differences from IpcSample.ConsoleServer:
//   - Console logging is enabled (visible in pytest output).
//   - WaitForStart() is awaited before printing the READY marker, so the
//     Python fixture can rely on the pipe actually accepting connections.
//   - No callback or message-parameter dependencies in the handlers, so
//     every method works against a callback-less Python client.
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
}

public interface ISystemService
{
    Task<string> EchoString(string value, CancellationToken ct = default);
    Task<byte[]> ReverseBytes(byte[] data, CancellationToken ct = default);
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

    public Task<byte[]> ReverseBytes(byte[] data, CancellationToken ct)
    {
        _logger.LogInformation("ReverseBytes(len={Length})", data.Length);
        var copy = (byte[])data.Clone();
        Array.Reverse(copy);
        return Task.FromResult(copy);
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
            .BuildServiceProvider();

        await using var server = new IpcServer
        {
            Transport = new NamedPipeServerTransport { PipeName = pipeName },
            ServiceProvider = serviceProvider,
            Endpoints = new()
            {
                typeof(IComputingService),
                typeof(ISystemService),
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
