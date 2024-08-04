using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

public sealed class SystemTestsOverNamedPipes : SystemTests
{
    private string PipeName => Names.GetPipeName(role: "system", TestRunId);

    protected override ListenerConfig CreateListener() => CommonConfigListener(new NamedPipeListener()
    {
        PipeName = PipeName
    });

    protected override ClientBase CreateClient() => CommonConfigClient(new NamedPipeClient()
    {
        PipeName = PipeName,
        AllowImpersonation = true,
    });
}
