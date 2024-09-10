namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record EnsureConnectionSucceeded : RecordBase, Is<Success>
    {
        public required Id<EnsureConnection> EnsureConnectionId { get; init; }

        public required string ConnectionDebugName { get; init; }

        public required bool NewlyCreated { get; init; }

        Id? Is<Success>.Of => EnsureConnectionId;
    }
}
