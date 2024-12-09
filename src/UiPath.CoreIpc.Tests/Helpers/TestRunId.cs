namespace UiPath.CoreIpc.Tests;

public readonly record struct TestRunId(Guid Value)
{
    public static TestRunId New() => new(Guid.NewGuid());
}

public static class ProcessHelper
{
    public static async Task<string> RunReturnStdOut(this Process process)
    {
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        TaskCompletionSource<object?> tcsProcessExited = new();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcsProcessExited.SetResult(null);

        _ = process.Start();
        await tcsProcessExited.Task;

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        if (process.ExitCode is not 0)
        {
            var stdErr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"The process exited with a non zero code. ExitCode={process.ExitCode}\r\nStdOut:\r\n{stdOut}\r\n\r\nStdErr:\r\n{stdErr}");
        }

        return stdOut;
    }
}