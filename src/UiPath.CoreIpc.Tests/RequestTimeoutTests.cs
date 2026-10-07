using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

/// <summary>
/// A handler that outlives the server's request timeout still runs to completion, but
/// <c>Server.OnRequestReceived</c> then sends its response on the very token the timeout just canceled.
/// The send throws, the <c>when (response is null)</c> filter does not match because the handler
/// succeeded, and the request is consumed without ever being answered - on a connection that stays
/// perfectly healthy.
/// </summary>
public abstract class RequestTimeoutTests : TestBase
{
    #region " Setup "
    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HandlerWork = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(10);

    private readonly Lazy<ISlowService?> _proxy;

    protected ISlowService Proxy => _proxy.Value!;

    protected sealed override IpcProxy IpcProxy => Proxy as IpcProxy ?? throw new InvalidOperationException($"Proxy was expected to be a {nameof(IpcProxy)} but was not.");
    protected sealed override Type ContractType => typeof(ISlowService);

    protected RequestTimeoutTests(ITestOutputHelper outputHelper) : base(outputHelper)
    {
        CreateLazyProxy(out _proxy);
    }

    protected override void ConfigureSpecificServices(IServiceCollection services)
    => services
        .AddSingleton<SlowService>()
        .AddSingletonAlias<ISlowService, SlowService>();

    /// <summary>Deliberately far shorter than <see cref="HandlerWork"/>.</summary>
    protected override TimeSpan ServerRequestTimeout => ServerTimeout;

    protected override void ConfigureClient(IpcClient ipcClient)
    {
        base.ConfigureClient(ipcClient);

        // The client must send NO timeout. Request.GetTimeout makes a client supplied timeout win over
        // the server's, so any value here would silently replace the ServerRequestTimeout under test and
        // the bug would not reproduce. The lease below is what stops the test hanging forever instead.
        ipcClient.RequestTimeout = null;
    }
    #endregion

    [Fact]
    public async Task CompletedHandler_OutlivingTheRequestTimeout_StillGetsItsResponse()
    {
        Steps.Reset();
        Steps.Log($"CLIENT  [1] server request timeout = {ServerTimeout.TotalSeconds:0.#}s, client RequestTimeout = none (waits forever)");
        Steps.Log($"CLIENT  [2] handler will take {HandlerWork.TotalSeconds:0.#}s, i.e. it outlives the server deadline");
        Steps.Log($"CLIENT  [3] sending request, awaiting the response with a {Lease.TotalSeconds:0.#}s test lease");

        try
        {
            var result = await Proxy
                .EchoAfterIgnoringCancellation("payload", HandlerWork)
                .ShouldCompleteInAsync(Lease);

            Steps.Log($"CLIENT  [7] response received: \"{result}\"");
            result.ShouldBe("payload");
        }
        catch (Exception ex)
        {
            Steps.Log($"CLIENT  [7] NO RESPONSE EVER ARRIVED - {ex.GetType().Name} after the {Lease.TotalSeconds:0.#}s lease");
            await ProbeConnectionStillAlive();
            throw;
        }
        finally
        {
            _outputHelper.WriteLine("");
            _outputHelper.WriteLine("======================= STEP TRACE =======================");
            foreach (var line in Steps.Drain())
            {
                _outputHelper.WriteLine(line);
            }
            _outputHelper.WriteLine("==========================================================");
        }
    }

    /// <summary>Shows the pipe is fine - the first answer was lost, not the connection.</summary>
    private async Task ProbeConnectionStillAlive()
    {
        try
        {
            var pong = await Proxy
                .EchoAfterIgnoringCancellation("ping", TimeSpan.Zero)
                .ShouldCompleteInAsync(TimeSpan.FromSeconds(5));
            Steps.Log($"CLIENT  [8] a second call on the SAME connection returned \"{pong}\" - the pipe was healthy all along");
        }
        catch (Exception ex)
        {
            Steps.Log($"CLIENT  [8] the probe call also failed: {ex.GetType().Name}");
        }
    }
}
