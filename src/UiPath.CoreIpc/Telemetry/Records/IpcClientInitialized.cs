namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record IpcClientInitialized : RecordBase, IExternallyTriggered
    {
        public required string Transport { get; init; }
    }
}
