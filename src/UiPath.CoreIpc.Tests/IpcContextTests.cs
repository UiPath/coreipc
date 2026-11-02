using Microsoft.Extensions.Logging;

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
        await using var pair = await CreatePair();
        (await pair.Proxy.ContextIsSet()).ShouldBeTrue();
    }

    [Fact]
    public async Task PocoContract_ReachesCallback_ViaIpcContext()
    {
        await using var pair = await CreatePair();
        (await pair.Proxy.ReachCallbackViaContext()).ShouldBe(ContextProbe.PongValue);
    }

    [Fact]
    public async Task Current_IsNullAgain_AfterTheCallCompletes()
    {
        await using var pair = await CreatePair();
        await pair.Proxy.ContextIsSet();
        // The ambient value must not leak into the test's own async flow.
        IpcContext.Current.ShouldBeNull();
    }

    private static Task<IpcPair<IContextProbe>> CreatePair()
        => IpcPair<IContextProbe>.Create(
            new ContextProbe(),
            configureClient: client => client.Callbacks = new() { { typeof(IContextProbeCallback), new ContextProbeCallback() } });
}
