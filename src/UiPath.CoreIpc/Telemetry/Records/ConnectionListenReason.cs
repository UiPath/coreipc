using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record ConnectionListenReason : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<ConnectionListenReason> Id => base.Id.Value;
        public Id<ServerConnectionListen>? ServerConnectionListenId { get; init; }
        public Id<ClientConnectionListen>? ClientConnectionListenId { get; init; }

        Id? Is<Effect>.Of => ServerConnectionListenId as Id ?? ClientConnectionListenId;
    }
}
