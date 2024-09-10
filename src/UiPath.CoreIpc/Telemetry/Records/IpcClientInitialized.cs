namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record IpcClientInitialized : RecordBase, IExternallyTriggered
    {
        public required string Transport { get; init; }
    }
}
