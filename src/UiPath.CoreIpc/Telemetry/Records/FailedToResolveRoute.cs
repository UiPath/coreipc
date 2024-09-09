namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record FailedToResolveRoute : RecordBase, IOperationEnd
    {
        public required Id<OnRequestReceived> RequestReceivedId { get; init; }

        Id IOperationEnd.StartId => RequestReceivedId;
    }
}
