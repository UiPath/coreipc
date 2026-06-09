// Test-only IPC *client* purpose-built for the Python uipath-ipc *server*
// integration suite — the reverse of IpcSample.PythonClientTestServer.
//
// A Python `IpcServer` (hosted in-process by the pytest fixture) listens on
// a named pipe; this .NET client connects and:
//   - calls service methods the Python server hosts (IPythonService),
//   - exercises an error path (RemoteException round-trip),
//   - exercises handler-initiated reach-back: the Python handler calls back
//     into THIS client's IClientCallback via message.client.get_callback(...).
//
// Pipe name is the first CLI argument. Prints "[PASS]"/"[FAIL]" per check and
// a final "ALL TESTS PASSED" marker; exit code = number of failed checks.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UiPath.Ipc;
using UiPath.Ipc.Transport.NamedPipe;

namespace IpcSample.PythonServerTestClient;

// Service hosted by the Python server. Names + parameter shapes must match
// the Python contract (the trailing CancellationToken is not sent on the
// wire, so the Python handler simply omits it).
public interface IPythonService
{
    Task<double> AddFloats(double x, double y, CancellationToken ct = default);
    Task<string> EchoString(string value, CancellationToken ct = default);
    Task<int> MultiplyInts(int x, int y, CancellationToken ct = default);
    Task<string> GreetVia(string name, CancellationToken ct = default);
    Task<bool> FailWith(string message, CancellationToken ct = default);
}

// Hosted by THIS client; the Python server's GreetVia handler calls it back.
public interface IClientCallback
{
    Task<string> Decorate(string name);
}

public sealed class ClientCallback : IClientCallback
{
    public Task<string> Decorate(string name) => Task.FromResult(name.ToUpperInvariant());
}

internal static class Program
{
    private static int _failures;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok)
        {
            Console.WriteLine($"[PASS] {name}");
        }
        else
        {
            _failures++;
            Console.WriteLine($"[FAIL] {name} {detail}");
        }
    }

    public static async Task Main(string[] args)
    {
        var pipeName = args.Length > 0 ? args[0] : "uipath-ipc-py-server-test";
        Console.WriteLine($"Connecting to Python server on pipe={pipeName}");

        await using var serviceProvider = new ServiceCollection()
            .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning))
            .BuildServiceProvider();

        var ipcClient = new IpcClient
        {
            Transport = new NamedPipeClientTransport { PipeName = pipeName },
            Callbacks = new() { { typeof(IClientCallback), new ClientCallback() } },
            ServiceProvider = serviceProvider,
            RequestTimeout = TimeSpan.FromSeconds(10),
        };

        try
        {
            var svc = ipcClient.GetProxy<IPythonService>();

            // 1. primitive round trip
            var sum = await svc.AddFloats(1.5, 2.5);
            Check("AddFloats", sum == 4.0, $"got {sum}");

            // 2. string round trip
            var echo = await svc.EchoString("hello from .NET");
            Check("EchoString", echo == "hello from .NET", $"got '{echo}'");

            // 3. int round trip
            var product = await svc.MultiplyInts(6, 7);
            Check("MultiplyInts", product == 42, $"got {product}");

            // 4. reach-back: Python handler calls THIS client's IClientCallback
            var greeting = await svc.GreetVia("bob");
            Check("GreetVia reach-back", greeting == "hello BOB", $"got '{greeting}'");

            // 5. error path: Python handler raises -> RemoteException here
            try
            {
                await svc.FailWith("kaboom");
                Check("FailWith raises", false, "no exception thrown");
            }
            catch (RemoteException ex)
            {
                Check("FailWith raises", ex.Message.Contains("kaboom"), $"msg='{ex.Message}' type='{ex.Type}'");
            }
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"[FAIL] unexpected exception: {ex}");
        }

        if (_failures == 0)
        {
            Console.WriteLine("ALL TESTS PASSED");
        }
        else
        {
            Console.WriteLine($"{_failures} CHECK(S) FAILED");
        }

        // Force a prompt exit regardless of lingering connection resources —
        // the pytest fixture waits on this process to terminate.
        Console.Out.Flush();
        Environment.Exit(_failures);
    }
}
