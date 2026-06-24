using System.IO.Pipes;

namespace UiPath.Ipc.Tests;

public sealed partial class NamedPipeSmokeTests
{
    // Regression test for the accept-loop teardown noise: disposing an IpcServer
    // tears down the idle named-pipe accept slots parked in WaitForConnectionAsync.
    // That can surface as a non-OCE IOException ("Pipe is broken") which used to be
    // reported via OnError -> Trace.TraceError("Failed to accept new connection...").
    // A clean shutdown must NOT log that. Real named pipes + real OS clients killed
    // abruptly (no mocks); captures the real System.Diagnostics.Trace error stream.
    [Fact]
    public async Task DisposingServerDoesNotLogFailedAcceptOnTeardown()
    {
        var pipeName = $"ipctest_{Guid.NewGuid():N}";
        using var capture = new TraceErrorCapture();

        await using (var ipcServer = CreateServer(pipeName))
        {
            ipcServer.Start();

            // A real client establishes a working connection (accept loop cycles).
            var ipcClient = CreateClient(pipeName);
            await ipcClient.GetProxy<IComputingService>().AddFloats(2, 3).ShouldBeAsync(5);

            // Simulate the executor being killed: connect real OS pipe clients and
            // drop them abruptly (close the handle without a graceful disconnect),
            // churning the accept slots, then tear the server down underneath them.
            for (int i = 0; i < 5; i++)
            {
                // `using` (not an explicit Dispose) is exception-safe; the
                // scope-end dispose is still an abrupt OS handle close.
                using var raw = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await raw.ConnectAsync(5_000);
            }

            await Task.Delay(Timeouts.Short); // let the accept slots re-park before teardown
        } // DisposeAsync awaits the accept-loop teardown, so any OnError has fired by here

        capture.AcceptErrors.ShouldBeEmpty();
    }

    private sealed class TraceErrorCapture : TraceListener
    {
        private readonly List<string> _errors = new();

        public TraceErrorCapture() => Trace.Listeners.Add(this);

        public string[] AcceptErrors
        {
            get { lock (_errors) return _errors.Where(e => e.Contains("Failed to accept new connection")).ToArray(); }
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            if (eventType == TraceEventType.Error && message is not null)
            {
                lock (_errors) _errors.Add(message);
            }
        }

        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Trace.Listeners.Remove(this);
            base.Dispose(disposing);
        }
    }
}
