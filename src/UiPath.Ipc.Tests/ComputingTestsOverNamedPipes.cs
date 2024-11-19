using UiPath.Ipc.Transport.NamedPipe;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public sealed class ComputingTestsOverNamedPipes : ComputingTests
{
    private string PipeName => Names.GetPipeName(role: "computing", TestRunId);

    public ComputingTestsOverNamedPipes(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected override async Task<ServerTransport> CreateListener() => new NamedPipeListener
    {
        PipeName = PipeName
    };
    protected override ClientTransport CreateClientTransport() => new NamedPipeTransport
    {
        PipeName = PipeName,
        AllowImpersonation = true,
    };

    public override IAsyncDisposable? RandomTransportPair(out ServerTransport listener, out ClientTransport transport)
    {
        var pipeName = $"{Guid.NewGuid():N}";
        listener = new NamedPipeListener { PipeName = pipeName };
        transport = new NamedPipeTransport { PipeName = pipeName };
        return null;
    }

    public override ExternalServerParams RandomServerParams()
    => new(ServerKind.NamedPipes, PipeName: $"{Guid.NewGuid():N}");
}
