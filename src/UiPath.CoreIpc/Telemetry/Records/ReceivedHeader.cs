using Newtonsoft.Json;
namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record ReceivedHeader : RecordBase, IOperationStart, Is<Effect>, ILoggable
    {
        [JsonIgnore]
        public new Id<ReceivedHeader> Id => base.Id.Value;
        public required Id<ReceiveLoop> ReceiveLoopId { get; init; }

        public required int MessageLength { get; init; }
        public required MessageType MessageType { get; init; }
        public required int MaxMessageLength { get; init; }
        public required bool SynchronizationContextIsNull { get; init; }

        Id? Is<Effect>.Of => ReceiveLoopId;

        [JsonIgnore]
        public ILogger? Logger { get; set; }

        [JsonIgnore]
        public string LogMessage => $"ReceiveHeader: {nameof(MessageType)}={MessageType}, {nameof(MessageLength)}={MessageLength}";

        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Information;
    }
}
