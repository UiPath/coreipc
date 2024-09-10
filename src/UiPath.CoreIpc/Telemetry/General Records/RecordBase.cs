namespace UiPath.Ipc;

partial class Telemetry
{
    public abstract partial record RecordBase : IDisposable
    {
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
        public Id Id { get; init; } = new UntypedId($"{Guid.NewGuid():N}");

        public string MemberName { get; private set; }
        public string FilePath { get; private set; }
        public int Line { get; private set; }
        public string StackTrace { get; private set; }

        internal IDisposable? Pop { get; set; }

        public RecordBase(
            string memberName,
            string filePath,
            int line,
            string stackTrace)
        {
            MemberName = memberName;
            FilePath = filePath;
            Line = line;
            StackTrace = stackTrace ?? new StackTrace().ToString();
        }

        public void Dispose() => Pop?.Dispose();
    }
}
