using System.Diagnostics;

namespace UiPath.Ipc.Tests;

/// <summary>Temporary step tracing, so the chronology of the dropped response is observable.</summary>
internal static class Steps
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Lines = new();

    public static void Reset()
    {
        while (Lines.TryDequeue(out _)) { }
        Clock.Restart();
    }

    public static void Log(string message) => Lines.Enqueue($"t+{Clock.Elapsed.TotalSeconds,5:0.00}s  {message}");

    public static string[] Drain() => Lines.ToArray();
}

public sealed class SlowService : ISlowService
{
    public async Task<string> EchoAfterIgnoringCancellation(string value, TimeSpan waitOnServer)
    {
        Steps.Log($"SERVER  [4] handler entered for \"{value}\" - will work {waitOnServer.TotalSeconds:0.#}s, takes no CancellationToken parameter");

        // Read-only peek at the ambient token purely so we can SEE it fire. The handler still does not
        // act on it, so the scenario is unchanged - this is exactly what OpenProject does.
        var ambient = IpcContext.Current?.CancellationToken ?? default;
        using var registration = ambient.Register(
            () => Steps.Log("SERVER  [5] *** request timeout FIRED - token canceled, but nobody is observing it, so the work continues ***"));

        await Task.Delay(waitOnServer); // deliberately no token

        Steps.Log("SERVER  [6] handler RAN TO COMPLETION - the answer now exists; Server is about to call SendResponse(response, token)");
        return value;
    }
}
