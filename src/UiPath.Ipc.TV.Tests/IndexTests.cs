using Newtonsoft.Json;
using UiPath.Ipc.Transport.NamedPipe;
using UiPath.Ipc.TV.Tests.Properties;

namespace UiPath.Ipc.TV.Tests;

public class IndexTests : IAsyncLifetime
{
    private DirectoryInfo _workdir = null!;

    //[Fact]
    public async Task Test1()
    {
        WriteResource(Resources.UiPath_Executor_19580, "UiPath.Executor-19580.temporary.ndjson");
        WriteResource(Resources.UiPath_Service_Host_54268, "UiPath.Service.Host-54268.temporary.ndjson");
        WriteResource(Resources.UiPath_Service_UserHost_37324, "UiPath.Service.UserHost-37324.temporary.ndjson");

        var relIndex = await RelationalIndexBuilder.Build(_workdir);
    }

    private void WriteResource(string contents, string fileName)
    {
        var filePath = Path.Combine(_workdir.FullName, fileName);
        File.WriteAllText(filePath, contents);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        _workdir = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(), 
            Path.GetRandomFileName()));
        _workdir.Create();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        _workdir.Delete(recursive: true);
    }
}