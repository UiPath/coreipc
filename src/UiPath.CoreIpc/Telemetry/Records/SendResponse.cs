namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record SendResponse : RecordBase, IOperationStart, Is<SubOperation>
    {
        public required Id<OnRequestReceived> OnRequestReceivedId { get; init; }

        Id? Is<SubOperation>.Of => OnRequestReceivedId;
    }
}
