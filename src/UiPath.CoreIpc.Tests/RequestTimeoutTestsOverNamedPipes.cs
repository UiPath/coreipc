using UiPath.Ipc.Transport.NamedPipe;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public sealed class RequestTimeoutTestsOverNamedPipes : RequestTimeoutTests
{
    private string PipeName => Names.GetPipeName(role: "requestTimeout", TestRunId);

    public RequestTimeoutTestsOverNamedPipes(ITestOutputHelper outputHelper) : base(outputHelper) { }

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
