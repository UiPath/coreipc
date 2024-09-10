using Newtonsoft.Json;
namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record OnRequestReceived : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<OnRequestReceived> Id => base.Id.Value;
        public required Id<HonorRequest> HonorRequest { get; init; }

        Id? Is<Effect>.Of => HonorRequest;
    }
}
