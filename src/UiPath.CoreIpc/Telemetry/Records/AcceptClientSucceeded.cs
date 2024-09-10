using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record AcceptClientSucceeded : VoidSucceeded
    {
        [JsonIgnore]
        public new Id<AcceptClientSucceeded> Id => base.Id.Value;
    }
}
