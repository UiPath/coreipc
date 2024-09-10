namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record ServiceInstanceRetrieved : RecordBase, Is<Effect>
    {
        public required Id<HandleRequest> HandleRequestId { get; init; }

        public required string? TypeName { get; init; }
        public required int ObjectHashCode { get; init; }

        Id? Is<Effect>.Of => HandleRequestId;
    }
}
