namespace UiPath.Ipc;

partial class Telemetry
{
    public partial record ResultSucceeded : RecordBase, IOperationEnd, Is<Success>
    {
        public required Id StartId { get; init; }
        public required string ResultJson { get; init; }

        Id? Is<Success>.Of => StartId;
    }
}
