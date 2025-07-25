#if NETCOREAPP
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace UiPath.Ipc.Tests;

partial class NamedPipeSmokeTests
{
    [Fact]
    public async Task PipeBreakAfterHeaderShouldNotInduceNRE()
    {
        var pipeName = $"ipctest_{Guid.NewGuid():N}";

        var tcsDetectedNRE = new TaskCompletionSource();

        await using (var ipcServer = CreateServer(pipeName, builder => builder
            .ClearProviders()
            .AddFakeLogging(options =>
            {
                options.OutputSink = _ => { };
                options.OutputFormatter = record =>
                {
                    if (IsNRE(record))
                    {
                        tcsDetectedNRE.TrySetResult();
                    }
                    return string.Empty;
                };
            })))
        {
            var ipcClient = CreateClient(pipeName, cMaxWrite: IOHelpers.HeaderLength);

            var proxy = ipcClient.GetProxy<IComputingService>();

            ipcServer.Start();
            proxy.AddFloats(2, 3).TraceError();

            await Task.Delay(TimeSpan.FromSeconds(1));
            (proxy as IpcProxy)?.Dispose();
        }

        await tcsDetectedNRE.Task
            .WaitAsync(TimeSpan.FromSeconds(10))
            .ShouldThrowAsync<TimeoutException>($"Expected the wait to time out with no {nameof(NullReferenceException)}, but the exception appeared instead before the deadline.");

        static bool IsNRE(FakeLogRecord record)
            => record.Exception is NullReferenceException ||
            (record.Level is LogLevel.Error && record.Message.Contains(nameof(NullReferenceException)));
    }
}

#endif

