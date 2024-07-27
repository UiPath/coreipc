using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using UiPath.Ipc.BackCompat;
using UiPath.Ipc.Transport.NamedPipe;
namespace UiPath.Ipc.Tests;

public class SystemNamedPipeTests : SystemTests<NamedPipeClientBuilder<ISystemService>>
{
    string _pipeName = "system";
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseNamedPipes(Configure(new NamedPipeListener()
        {
            PipeName = _pipeName + GetHashCode()
        }));
    protected override NamedPipeClientBuilder<ISystemService> CreateSystemClientBuilder() =>
        new NamedPipeClientBuilder<ISystemService>(_pipeName + GetHashCode()).AllowImpersonation();
    [Fact]
    public void PipeExists()
    {
        IOHelpers.PipeExists(System.Guid.NewGuid().ToString()).ShouldBeFalse();
        IOHelpers.PipeExists("system" + GetHashCode(), 50).ShouldBeTrue();
    }
    [Fact]
    public Task ServerName() => SystemClientBuilder().ValidateAndBuild().EchoGuid(System.Guid.Empty);
    [Fact]
    public override void BeforeCallServerSide()
    {
        _pipeName = "beforeCall";
        base.BeforeCallServerSide();
    }
#if WINDOWS
    [Fact]
    public async Task PipeSecurityForWindows()
    {
        _pipeName = "protected";
        await using var protectedService = new ServiceHostBuilder(_serviceProvider)
            .UseNamedPipes(Configure(new NamedPipeListener()
            {
                PipeName = _pipeName + GetHashCode(),
                AccessControl = pipeSecurity => pipeSecurity.Deny(WellKnownSidType.WorldSid, PipeAccessRights.FullControl)
            }))
            .AddEndpoint<ISystemService>()
            .ValidateAndBuild();
        _ = protectedService.RunAsync();
        await CreateSystemService().FireAndForget().ShouldThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(100)]
    [InlineData(1100)]
    [InlineData(100, 100)]
    public async Task PipeCancelIoOnServer_AnyNoOfTimes(params int[] msDelays)
    {
        bool cancelIoResult = false;

        // Any number of kernel32.CancelIoEx calls should not cause the client to give up on the connection.
        var act = async () => cancelIoResult = await _systemClient.CancelIoPipe(new() { MsDelays = msDelays });
        await act.ShouldNotThrowAsync();

        //Make sure the connection is still working
        (await _systemClient.Delay()).ShouldBeTrue();

        cancelIoResult.ShouldBeTrue();
    }

    [Fact]
    public async Task PipeCancelIoOnClient()
    {
        (await _systemClient.Delay()).ShouldBeTrue();

        var delayTask = _systemClient.Delay(500);
        await Task.Delay(100);
        var pipeStream = ((IpcProxy)_systemClient).Network as PipeStream;
        SystemService.CancelIoEx(pipeStream.SafePipeHandle.DangerousGetHandle(), IntPtr.Zero).ShouldBeTrue();

        (await delayTask).ShouldBeTrue();

        //Make sure the connection is still working
        (await _systemClient.Delay()).ShouldBeTrue();
    }
#endif
}
public class ComputingNamedPipeTests : ComputingTests<NamedPipeClientBuilder<IComputingService, IComputingCallback>>
{
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseNamedPipes(Configure(new NamedPipeListener { PipeName = "computing" + GetHashCode() }));

    protected override NamedPipeClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null) =>
    new NamedPipeClientBuilder<IComputingService, IComputingCallback>("computing" + GetHashCode(), _serviceProvider)
        .AllowImpersonation()
        .RequestTimeout(RequestTimeout)
        .CallbackInstance(_computingCallback)
        .TaskScheduler(taskScheduler);
}