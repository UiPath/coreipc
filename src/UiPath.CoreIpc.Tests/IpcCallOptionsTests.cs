using Microsoft.Extensions.DependencyInjection;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

// A POCO contract: no `Message` parameter, yet the caller can still bound the call.
public interface ISlowProbe
{
    Task<string> Slow();
}

public sealed class SlowProbe : ISlowProbe
{
    public async Task<string> Slow()
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        return "done";
    }
}

public sealed class IpcCallOptionsTests
{
    [Fact]
    public void Current_IsNull_OutsideAnyScope()
        => IpcCallOptions.Current.ShouldBeNull();

    [Fact]
    public void With_SetsAndRestores_AndNests()
    {
        IpcCallOptions.Current.ShouldBeNull();
        using (IpcCallOptions.With(TimeSpan.FromSeconds(7)))
        {
            IpcCallOptions.Current!.RequestTimeout.ShouldBe(TimeSpan.FromSeconds(7));
            using (IpcCallOptions.With(TimeSpan.FromSeconds(3)))
            {
                IpcCallOptions.Current!.RequestTimeout.ShouldBe(TimeSpan.FromSeconds(3));
            }
            IpcCallOptions.Current!.RequestTimeout.ShouldBe(TimeSpan.FromSeconds(7));
        }
        IpcCallOptions.Current.ShouldBeNull();
    }

    [Fact]
    public void With_RestoresOnThrow()
    {
        try
        {
            using (IpcCallOptions.With(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("boom");
            }
        }
        catch (InvalidOperationException)
        {
        }

        IpcCallOptions.Current.ShouldBeNull();
    }

    [Fact]
    public async Task AmbientTimeout_BoundsACallOnAPocoContract()
    {
        await using var pair = await Pair.Create();

        // The contract takes no Message, so before this the caller had no way to bound the call.
        using (IpcCallOptions.With(TimeSpan.FromMilliseconds(200)))
        {
            await Should.ThrowAsync<TimeoutException>(() => pair.Proxy.Slow());
        }
    }

    [Fact]
    public async Task WithoutTheScope_TheSameCallIsNotBounded()
    {
        await using var pair = await Pair.Create();

        var call = pair.Proxy.Slow();
        var finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(1)));
        finished.ShouldNotBe(call, "the call must still be in flight, i.e. unbounded");
    }

    private sealed class Pair : IAsyncDisposable
    {
        private readonly IpcServer _server;
        public ISlowProbe Proxy { get; }

        private Pair(IpcServer server, ISlowProbe proxy)
        {
            _server = server;
            Proxy = proxy;
        }

        public static async Task<Pair> Create()
        {
            var pipeName = $"ipctest_opts_{Guid.NewGuid():N}";

            var server = new IpcServer
            {
                Transport = new NamedPipeServerTransport { PipeName = pipeName },
                Endpoints = new() { typeof(ISlowProbe) },
                ServiceProvider = new ServiceCollection()
                    .AddLogging()
                    .AddSingleton<ISlowProbe, SlowProbe>()
                    .BuildServiceProvider(),
            };

            var client = new IpcClient
            {
                Transport = new NamedPipeClientTransport { PipeName = pipeName },
            };
            var proxy = client.GetProxy<ISlowProbe>();

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
