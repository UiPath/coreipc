using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record DeserializePayload : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<DeserializePayload> Id => base.Id.Value;
        public required Id<ReceivedHeader>? ReceivedHeaderId { get; init; }

        Id? Is<Effect>.Of => ReceivedHeaderId;
    }
}
