using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record DeserializationSucceeded : ResultSucceeded, ILoggable
    {
        [JsonIgnore]
        public new Id<DeserializationSucceeded> Id => base.Id.Value;

        [JsonIgnore]
        public ILogger? Logger { get; set; }
        [JsonIgnore]
        public string LogMessage => $"Deserialization succeeded. Result is {ResultJson}";
        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Information;
    }
}
