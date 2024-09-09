namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record RunOnScheduler : RecordBase, IOperationStart, Is<Effect>
    {
        public required Id<InvokeLocal> InvokeMethodId { get; init; }

        public required string SchedulerTypeName { get; init; }

        Id? Is<Effect>.Of => InvokeMethodId;
    }
}
