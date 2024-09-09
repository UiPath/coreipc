using Newtonsoft.Json;
namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record ReceiveLoop : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<ReceiveLoop> Id => base.Id.Value;
        public required Id<ConnectionListenReason> ConnectionListenReasonId { get; init; }

        Id? Is<Effect>.Of => ConnectionListenReasonId;
    }
}
