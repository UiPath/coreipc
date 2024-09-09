namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record ProcessStart : RecordBase
    {
        private static readonly object Lock = new();
        private static bool Initialized = false;
        internal static void EnsureInitialized()
        {
            lock (Lock)
            {
                if (Initialized)
                {
                    return;
                }
                Initialized = true;
                Log(new ProcessStart());
            }
        }

        public string Name { get; init; } = CurrentProcessInfo.Name;
        public int ProcessId { get; init; } = CurrentProcessInfo.Id;
        public string Path { get; init; } = CurrentProcessInfo.Path;
        public string CommandLine { get; init; } = CurrentProcessInfo.CommandLine;
        public string Framework { get; init; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        public bool Is64BitProcess { get; init; } = Environment.Is64BitProcess;
        public bool Is64BitOS { get; init; } = Environment.Is64BitOperatingSystem;
    }
}
