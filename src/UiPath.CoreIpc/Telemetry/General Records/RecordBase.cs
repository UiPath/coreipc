using Newtonsoft.Json;

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
        public IReadOnlyList<string> StackTrace { get; private set; }

        internal IDisposable? Pop { get; set; }

        [JsonConstructor]
        private RecordBase(string memberName, string filePath, int line, IReadOnlyList<string> stackTrace)
        {
            MemberName = memberName;
            FilePath = filePath;
            Line = line;
            StackTrace = stackTrace;
        }

        [DebuggerHidden]
        public RecordBase([CallerMemberName] string memberName = null!, [CallerFilePath] string filePath = null!, [CallerLineNumber] int line = 0)
        {
            MemberName = memberName;
            FilePath = filePath;
            Line = line;
            StackTrace = new StackTrace().GetFrames().Skip(1).Select(x => x.ToString()).ToArray();
        }

        public void Dispose() => Pop?.Dispose();
    }
}
