using Newtonsoft.Json;
namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record ReceiveLoop : RecordBase, IOperationStart, Is<Effect>, ILoggable
    {
        [JsonIgnore]
        public new Id<ReceiveLoop> Id => base.Id.Value;
        public required Id<ConnectionListenReason> ConnectionListenReasonId { get; init; }

        Id? Is<Effect>.Of => ConnectionListenReasonId;

        [JsonIgnore]
        public ILogger? Logger { get; set; }

        [JsonIgnore]
        public string LogMessage => $"ReceiveLoop started...";

        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Information;
    }
}
