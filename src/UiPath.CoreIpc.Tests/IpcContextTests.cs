using Microsoft.Extensions.Logging;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

// A POCO contract: no `Message` parameter, yet callback-capable via `IpcContext.Current`.
public interface IContextProbe
{
    Task<string> ReachCallbackViaContext();
    Task<bool> ContextIsSet();
}

public interface IContextProbeCallback
{
    Task<string> Pong();
}

public sealed class ContextProbe : IContextProbe
{
    public const string PongValue = "pong-from-callback";

    // No Message parameter anywhere: the peer is reached through the ambient context.
    public Task<string> ReachCallbackViaContext()
        => IpcContext.Current!.GetCallback<IContextProbeCallback>().Pong();

    public Task<bool> ContextIsSet() => Task.FromResult(IpcContext.Current is not null);
}

public sealed class ContextProbeCallback : IContextProbeCallback
{
    public Task<string> Pong() => Task.FromResult(ContextProbe.PongValue);
}

public sealed class IpcContextTests
{
    [Fact]
    public void Current_IsNull_OutsideAnyIpcCall()
        => IpcContext.Current.ShouldBeNull();

    [Fact]
    public async Task Current_IsSet_WhileHonoringACall()
    {
        await using var pair = await Pair.Create();
        (await pair.Proxy.ContextIsSet()).ShouldBeTrue();
    }

    [Fact]
    public async Task PocoContract_ReachesCallback_ViaIpcContext()
    {
        await using var pair = await Pair.Create();
        (await pair.Proxy.ReachCallbackViaContext()).ShouldBe(ContextProbe.PongValue);
    }

    [Fact]
    public async Task Current_IsNullAgain_AfterTheCallCompletes()
    {
        await using var pair = await Pair.Create();
        await pair.Proxy.ContextIsSet();
        // The ambient value must not leak into the test's own async flow.
        IpcContext.Current.ShouldBeNull();
    }

    private sealed class Pair : IAsyncDisposable
    {
        private readonly IpcServer _server;
        public IContextProbe Proxy { get; }

        private Pair(IpcServer server, IContextProbe proxy)
        {
            _server = server;
            Proxy = proxy;
        }

        public static async Task<Pair> Create()
        {
            var pipeName = $"ipctest_ctx_{Guid.NewGuid():N}";

            var server = new IpcServer
            {
                Transport = new NamedPipeServerTransport { PipeName = pipeName },
                Endpoints = new() { typeof(IContextProbe) },
                ServiceProvider = new ServiceCollection()
                    .AddLogging()
                    .AddSingleton<IContextProbe, ContextProbe>()
                    .BuildServiceProvider(),
            };

            var client = new IpcClient
            {
                Transport = new NamedPipeClientTransport { PipeName = pipeName },
                Callbacks = new() { { typeof(IContextProbeCallback), new ContextProbeCallback() } },
            };
            var proxy = client.GetProxy<IContextProbe>();

            server.Start();
            await Task.Yield();
            return new Pair(server, proxy);
        }

        public async ValueTask DisposeAsync()
        {
            (Proxy as IpcProxy)?.Dispose();
            await ((Proxy as IpcProxy)?.CloseConnection() ?? default);
            await _server.DisposeAsync();
        }
    }
}
