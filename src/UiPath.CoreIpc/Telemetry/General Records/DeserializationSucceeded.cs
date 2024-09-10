using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record DeserializationSucceeded : ResultSucceeded
    {
        [JsonIgnore]
        public new Id<DeserializationSucceeded> Id => base.Id.Value;
    }
}
