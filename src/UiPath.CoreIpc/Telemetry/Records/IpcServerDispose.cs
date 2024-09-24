
using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record IpcServerDispose : RecordBase, IVoidOperation, ILoggable
    {
        [JsonIgnore]
        public ILogger? Logger { get; set; }
        [JsonIgnore]
        public string LogMessage => "IpcServer.Dispose: start";
        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Information;

        public VoidSucceeded CreateSucceeded() => new IpcServerDisposeSucceeded { StartId = Id, StartedAtUtc = CreatedAtUtc };

        public VoidFailed CreateFailed(Exception? ex) => new IpcServerDisposeFailed { StartId = Id, Exception = ex, StartedAtUtc = CreatedAtUtc };
    }

    public sealed partial record IpcServerDisposeSucceeded : VoidSucceeded, ILoggable
    {
        public required DateTime StartedAtUtc { get; init; }
        [JsonIgnore]
        public ILogger? Logger { get; set; }
        [JsonIgnore]
        public string LogMessage => $"IpcServer.Dispose: succeeded in {CreatedAtUtc - StartedAtUtc}";
        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Information;
    }

    public sealed partial record IpcServerDisposeFailed : VoidFailed, ILoggable
    {
        public required DateTime StartedAtUtc { get; init; }
        [JsonIgnore]
        public ILogger? Logger { get; set; }
        [JsonIgnore]
        public string LogMessage => $"IpcServer.Dispose: failed in {CreatedAtUtc - StartedAtUtc}";
        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Error;
    }
}
