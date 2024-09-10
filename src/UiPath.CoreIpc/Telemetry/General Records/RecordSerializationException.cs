namespace UiPath.Ipc;

partial class Telemetry
{
    public partial record RecordSerializationException : RecordBase
    {
        public required Id RecordId { get; init; }
        public required string RecordTypeName { get; init; }
        public required string? RecordToString { get; init; }
        public required ExceptionInfo ExceptionInfo { get; init; }
    }
}
