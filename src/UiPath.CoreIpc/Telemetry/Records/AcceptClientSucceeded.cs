using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record AcceptClientSucceeded : VoidSucceeded, ILoggable
    {
        [JsonIgnore]
        public new Id<AcceptClientSucceeded> Id => base.Id.Value;

        [JsonIgnore]
        public ILogger? Logger { get; set; }

        [JsonIgnore]
        public string LogMessage => "AcceptClientSucceeded";

        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Information;
    }

    public sealed partial record AcceptClientFailed : VoidFailed, ILoggable
    {
        [JsonIgnore]
        public new Id<AcceptClientSucceeded> Id => base.Id.Value;

        [JsonIgnore]
        public string ExceptionToString { get; set; }

        [JsonIgnore]
        public ILogger? Logger { get; set; }

        [JsonIgnore]
        public string LogMessage => $"AcceptClientFailed. Ex: {ExceptionToString}";

        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Error;
    }
}
