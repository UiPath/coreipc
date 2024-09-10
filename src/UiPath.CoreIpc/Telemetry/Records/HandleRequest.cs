using Newtonsoft.Json;
namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record HandleRequest : RecordBase, IOperationStart, Is<SubOperation>
    {
        [JsonIgnore]
        public new Id<HandleRequest> Id => base.Id.Value;
        public required Id<OnRequestReceived> OnRequestReceivedId { get; init; }

        Id? Is<SubOperation>.Of => OnRequestReceivedId;
    }
}
