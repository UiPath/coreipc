using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record ServerConnectionCreated : RecordBase, ISubOperation, IOperationStart, Is<SubOperation>
    {
        [JsonIgnore]
        public new Id<ServerConnectionCreated> Id => base.Id.Value;
        public required Id<RunListener> ParentId { get; init; }

        string ISubOperation.ParentId => ParentId.Value;
        Id? Is<SubOperation>.Of => ParentId;
    }
}
