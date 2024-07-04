using Microsoft.Extensions.Logging;
using UiPath.Ipc.Tests;

namespace IpcSample.ConsoleClient;

internal class SimpleClient
{

    public static async Task Entry()
    {
        Settings pf = new()
        {
            ClientTransport = new ClientTransport.NamedPipes
            {
                PipeName = "test",
                AllowImpersonation = true
            },
            Logger = new Logger<Settings>(new LoggerFactory()),
            RequestTimeout = TimeSpan.FromSeconds(2),
            Callback = new CallbackSource.Instance
            {
                CallbackInstance = new ComputingCallback { Id = "custom made" }
            }
        };

        var cs = pf.Build<IComputingService>();
        // -----------
    }

    class Settings
    {
        public required ClientTransport ClientTransport { get; init; }
        public TimeSpan RequestTimeout { get; init; } = Timeout.InfiniteTimeSpan;
        public required ILogger Logger { get; init; }
        public CallbackSource? Callback { get; init; }

        public T Build<T>() where T : class
        {
            throw null!;
        }
    }

    abstract class CallbackSource
    {
        public class Injected : CallbackSource
        {
            public required IServiceProvider ServiceProvider { get; init; }
            public required Type CallbackType { get; init; }
        }

        public class Instance : CallbackSource
        {
            public required object CallbackInstance { get; init; }
        }
    }

    abstract class ClientTransport
    {
        public class NamedPipes : ClientTransport
        {
            public required string PipeName { get; init; }
            public bool AllowImpersonation { get; init; }
        }
    }

}
