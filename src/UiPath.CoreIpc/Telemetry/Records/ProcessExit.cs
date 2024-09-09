namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record ProcessExit : RecordBase
    {
        static ProcessExit()
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                using var process = Process.GetCurrentProcess();
                var exitCode = process.ExitCode;

                Log(new ProcessExit
                {
                    ExitCode = exitCode,
                });
                Close();
            };
        }
        public required int ExitCode { get; init; }
    }
}
