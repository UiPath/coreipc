using System.IO.Pipes;
using System.Security.Principal;
namespace UiPath.CoreIpc.Tests;

public class SystemNamedPipeTests : SystemTests<NamedPipeClientBuilder<ISystemService>>
{
    string _pipeName = "system";
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseNamedPipes(Configure(new NamedPipeSettings(_pipeName+GetHashCode())));
    protected override NamedPipeClientBuilder<ISystemService> CreateSystemClientBuilder() => 
        new NamedPipeClientBuilder<ISystemService>(_pipeName+GetHashCode()).AllowImpersonation();
    [Fact]
    public void PipeExists()
    {
        IOHelpers.PipeExists(System.Guid.NewGuid().ToString()).ShouldBeFalse();
        IOHelpers.PipeExists("system"+GetHashCode(), 50).ShouldBeTrue();
    }
    [Fact]
    public Task ServerName() => SystemClientBuilder().ValidateAndBuild().GetGuid(System.Guid.Empty);
#if WINDOWS
    [Fact]
    public async Task PipeSecurityForWindows()
    {
        _pipeName = "protected";
        using var protectedService = new ServiceHostBuilder(_serviceProvider)
            .UseNamedPipes(Configure(new NamedPipeSettings(_pipeName+GetHashCode())
            {
                AccessControl = pipeSecurity => pipeSecurity.Deny(WellKnownSidType.WorldSid, PipeAccessRights.FullControl)
            }))
            .AddEndpoint<ISystemService>()
            .ValidateAndBuild();
        _ = protectedService.RunAsync();
        await CreateSystemService().DoNothing().ShouldThrowAsync<UnauthorizedAccessException>();
    }
#endif
}
public class ComputingNamedPipeTests : ComputingTests<NamedPipeClientBuilder<IComputingService, IComputingCallback>>
{
    protected override ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder) =>
        serviceHostBuilder.UseNamedPipes(Configure(new NamedPipeSettings("computing" + GetHashCode())));
    protected override NamedPipeClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null) =>
        new NamedPipeClientBuilder<IComputingService, IComputingCallback>("computing" + GetHashCode(), _serviceProvider)
            .AllowImpersonation()
            .RequestTimeout(RequestTimeout)
            .CallbackInstance(_computingCallback)
            .TaskScheduler(taskScheduler);
}