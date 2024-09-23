using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record DeserializePayload : RecordBase, IOperationStart, ILoggable, Is<Effect>
    {
        [JsonIgnore]
        public ILogger? Logger { get; set; }
        [JsonIgnore]
        public string LogMessage => $"DeserializePayload starting";
        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Information;

        [JsonIgnore]
        public new Id<DeserializePayload> Id => base.Id.Value;
        public required Id<ReceivedHeader>? ReceivedHeaderId { get; init; }

        Id? Is<Effect>.Of => ReceivedHeaderId;
    }
}
