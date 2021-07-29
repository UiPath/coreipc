using System;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading.Tasks;
using Shouldly;
using UiPath.CoreIpc.NamedPipe;
using Xunit;
namespace UiPath.CoreIpc.Tests
{
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
            IOHelpers.PipeExists("system"+GetHashCode(), 30).ShouldBeTrue();
        }
        [Fact]
        public Task ServerName() => SystemClientBuilder().ServerName(Environment.MachineName).ValidateAndBuild().GetGuid(System.Guid.Empty);
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
            serviceHostBuilder.UseNamedPipes(Configure(new NamedPipeSettings("computing"+GetHashCode()){ EncryptAndSign = true }));
        protected override NamedPipeClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null) =>
            new NamedPipeClientBuilder<IComputingService, IComputingCallback>("computing" + GetHashCode(), _serviceProvider)
                .AllowImpersonation()
                .EncryptAndSign()
                .RequestTimeout(RequestTimeout)
                .CallbackInstance(_computingCallback)
                .TaskScheduler(taskScheduler);
    }
}