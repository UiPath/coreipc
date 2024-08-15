using UiPath.Ipc.Transport.NamedPipe;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public sealed class ComputingTestsOverNamedPipes : ComputingTests
{
    private string PipeName => Names.GetPipeName(role: "computing", TestRunId);

    public ComputingTestsOverNamedPipes(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected override ListenerConfig CreateListener() => new NamedPipeListener()
    {
        PipeName = PipeName
    };
    protected override ClientTransport CreateClientTransport() => new NamedPipeTransport()
    {
        PipeName = PipeName,
        AllowImpersonation = true,
    };    
}
