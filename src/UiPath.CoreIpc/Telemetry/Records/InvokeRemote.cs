using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record InvokeRemote : RecordBase, IOperationStart, Is<SubOperation>
    {
        [JsonIgnore]
        public new Id<InvokeRemote> Id => base.Id.Value;
        public required Id<ServiceClientCreated> ServiceClientId { get; init; }

        public required string Method { get; init; }
        public required bool DefaultSynchronizationContext { get; init; }

        Id IOperationStart.Id => Id;
        Id? Is<SubOperation>.Of => ServiceClientId;
    }
}
