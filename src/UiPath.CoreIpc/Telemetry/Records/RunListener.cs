using Newtonsoft.Json;
namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record RunListener : RecordBase, IOperationStart
    {
        [JsonIgnore]
        public new Id<RunListener> Id => base.Id.Value;
        public required ListenerConfig Config { get; init; }
    }
}
