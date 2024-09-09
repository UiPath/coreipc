using System.Diagnostics;
using UiPath.Ipc.TV.DataAccess;

namespace UiPath.Ipc.TV.Tests;

public class SQLiteTests : IAsyncLifetime
{
    private readonly FileInfo _db = new(Path.ChangeExtension(Path.GetTempFileName(), ".sqlite"));

    //[Fact]
    public async Task Basic()
    {
        await using var context = TelemetryContextFactory.Create(_db.FullName);
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await TryDelete(_db, TimeSpan.FromSeconds(10));
    }

    private static async Task TryDelete(FileInfo file, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                file.Delete();
                return;
            }
            catch (Exception ex)
            {
                if (stopwatch.Elapsed > timeout)
                {
                    throw new TimeoutException(message: null, ex);
                }
                await Task.Delay(100);
            }
        }
    }
}
