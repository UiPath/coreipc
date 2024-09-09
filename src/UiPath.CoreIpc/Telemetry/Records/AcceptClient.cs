using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record AcceptClient : RecordBase, ISubOperation, IOperationStart, Is<SubOperation>
    {
        [JsonIgnore]
        public new Id<AcceptClient> Id => base.Id.Value;
        public required Id<ServerConnectionCreated> ParentId { get; init; }

        string ISubOperation.ParentId => ParentId;
        Id Is<SubOperation>.Of => ParentId;
    }
    
    public sealed record ServerConnectionCreated : RecordBase, ISubOperation, IOperationStart, Is<SubOperation>
    {
        [JsonIgnore]
        public new Id<ServerConnectionCreated> Id => base.Id.Value;
        public required Id<RunListener> ParentId { get; init; }

        string ISubOperation.ParentId => ParentId.Value;
        Id? Is<SubOperation>.Of => ParentId;
    }

    public sealed record ServerConnectionDisposed : VoidSucceeded
    {
        [JsonIgnore]
        public new Id<ServerConnectionDisposed> Id => base.Id.Value;
    }
}
