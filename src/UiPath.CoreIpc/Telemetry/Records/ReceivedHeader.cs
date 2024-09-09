using Newtonsoft.Json;
namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record ReceivedHeader : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<ReceivedHeader> Id => base.Id.Value;
        public required Id<ReceiveLoop> ReceiveLoopId { get; init; }

        public required int MessageLength { get; init; }
        public required MessageType MessageType { get; init; }
        public required int MaxMessageLength { get; init; }
        public required bool SynchronizationContextIsNull { get; init; }

        Id? Is<Effect>.Of => ReceiveLoopId;
    }
}
