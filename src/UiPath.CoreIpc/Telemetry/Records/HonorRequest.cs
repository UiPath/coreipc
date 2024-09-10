using Newtonsoft.Json;
namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record HonorRequest : HonorDeserialization
    {
        [JsonIgnore]
        public new Id<HonorRequest> Id => base.Id.Value;
    }
}
