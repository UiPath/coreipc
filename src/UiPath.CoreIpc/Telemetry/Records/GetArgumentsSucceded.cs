namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record GetArgumentsSucceded : RecordBase, Is<Effect>
    {
        public required Id<HandleRequest> HandleRequestId { get; init; }

        public required IReadOnlyList<string> TypeNames { get; init; }

        Id? Is<Effect>.Of => HandleRequestId;
    }
}
