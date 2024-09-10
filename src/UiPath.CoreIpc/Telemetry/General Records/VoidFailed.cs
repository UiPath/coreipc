namespace UiPath.Ipc;

partial class Telemetry
{
    public partial record VoidFailed : RecordBase, IOperationEnd, IOperationFailed, Is<Failure>
    {
        public required Id StartId { get; init; }
        public ExceptionInfo? Exception { get; init; }

        Id? Is<Failure>.Of => StartId;
    }
}
