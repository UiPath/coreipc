using Newtonsoft.Json;
namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record HonorRequest : HonorDeserialization, ILoggable
    {
        [JsonIgnore]
        public new Id<HonorRequest> Id => base.Id.Value;
        public required string Method { get; init; }

        [JsonIgnore]
        public ILogger? Logger { get; set; }
        [JsonIgnore]
        public string LogMessage => $"Honoring request for method {Method}";
        [JsonIgnore]
        public LogLevel LogLevel => LogLevel.Information;
    }
}
