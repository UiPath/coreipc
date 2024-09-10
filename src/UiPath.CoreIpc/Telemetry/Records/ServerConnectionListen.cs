using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record ServerConnectionListen : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<ServerConnectionListen> Id => base.Id.Value;
        public required Id<AcceptClientSucceeded> AcceptClientSucceededId { get; init; }

        Id? Is<Effect>.Of => AcceptClientSucceededId;
    }
}
