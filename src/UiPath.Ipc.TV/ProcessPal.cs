using System.Diagnostics;
using System.Text;

namespace UiPath.Ipc.TV;

internal static class ProcessPal
{
    public static async Task<string> Run(ProcessStartInfo startInfo, TaskScheduler? scheduler = null, IProgress<string?>? stdout = null, IProgress<string?>? stderr = null, CancellationToken ct = default)
    {
        startInfo.CreateNoWindow = true;
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = new Process { StartInfo = startInfo };
        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();
        process.OutputDataReceived += (sender, e) =>
        {
            sbOut.AppendLine(e.Data);
            scheduler.OrDefault().Run(() => stdout?.Report(e.Data));
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            sbErr.AppendLine(e.Data);
            scheduler.OrDefault().Run(() => stderr?.Report(e.Data));
        };
        process.EnableRaisingEvents = true;

        _ = process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == ct)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex2)
            {
                ex2.TraceError();
            }

            throw;
        }

        if (process.ExitCode is 0)
        {
            return sbOut.ToString();
        }

        throw new InvalidOperationException($"The process failed. Cmdline was \"{startInfo.FileName} {startInfo.Arguments}\".\r\nStdout:\r\n{sbOut}\r\n\r\nStderr:\r\n{sbErr}");
    }
}