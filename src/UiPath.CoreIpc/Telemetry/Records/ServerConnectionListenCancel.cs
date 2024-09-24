
using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record ServerConnectionListenCancel : RecordBase, Is<Modifier>, IOperationStart, ILoggable, IVoidOperation
    {
        public required Id<ServerConnectionListen> ServerConnectionListenId { get; init; }

        Id? Is<Modifier>.Of => ServerConnectionListenId;

        [JsonIgnore]
        public ILogger? Logger { get; set; }
        [JsonIgnore]
        public string LogMessage => "ServerConnectionListenCancel: start";
        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Information;

        public VoidSucceeded CreateSucceeded() => new ServerConnectionListenCancelSucceeded { StartId = Id, StartedAtUtc = CreatedAtUtc };
        public VoidFailed CreateFailed(Exception? ex) => new ServerConnectionListenCancelFailed { StartId = Id, Exception = ex, StartedAtUtc = CreatedAtUtc };
    }

    public partial record ServerConnectionListenCancelSucceeded : VoidSucceeded, ILoggable
    {
        public required DateTime StartedAtUtc { get; init; }

        [JsonIgnore]
        public ILogger? Logger { get; set; }

        [JsonIgnore]
        public string LogMessage => $"ServerConnectionListenCancel: succeeded in {CreatedAtUtc - StartedAtUtc}";

        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Information;
    }
    public partial record ServerConnectionListenCancelFailed : VoidFailed, ILoggable {
        public required DateTime StartedAtUtc { get; init; }

        [JsonIgnore]
        public ILogger? Logger { get; set; }

        [JsonIgnore]
        public string LogMessage => $"ServerConnectionListenCancel: failed in {CreatedAtUtc - StartedAtUtc}";

        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Error;
    }
}
