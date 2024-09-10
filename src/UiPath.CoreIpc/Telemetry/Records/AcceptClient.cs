using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record AcceptClient : RecordBase, ISubOperation, IOperationStart, Is<SubOperation>
    {
        [JsonIgnore]
        public new Id<AcceptClient> Id => base.Id.Value;
        public required Id<ServerConnectionCreated> ParentId { get; init; }

        string ISubOperation.ParentId => ParentId;
        Id Is<SubOperation>.Of => ParentId;
    }
}
