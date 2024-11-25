using UiPath.Ipc.Transport.NamedPipe;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public sealed class SystemTestsOverNamedPipes : SystemTests
{
    private string PipeName => Names.GetPipeName(role: "system", TestRunId);

    public SystemTestsOverNamedPipes(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected sealed override async Task<ServerTransport> CreateServerTransport() => new NamedPipeServerTransport
    {
        PipeName = PipeName
    };
    protected sealed override ClientTransport CreateClientTransport() => new NamedPipeClientTransport()
    {
        PipeName = PipeName,
        AllowImpersonation = true,
    };
}
