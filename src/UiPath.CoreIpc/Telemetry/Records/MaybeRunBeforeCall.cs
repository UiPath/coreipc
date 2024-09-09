namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record MaybeRunBeforeCall : RecordBase, IOperationStart, Is<Effect>
    {
        public required Id<InvokeLocal> InvokeMethodId { get; init; }

        public required bool BeforeCallIsNotNull { get; init; }

        public string? BeforeCallMethod { get; init; }
        public int? BeforeCallTargetObjectHashCode { get; init; }

        Id? Is<Effect>.Of => InvokeMethodId;
    }
}
