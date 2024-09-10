using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record EnsureConnection : RecordBase, IOperationStart, Is<Modifier>, Is<Effect>
    {
        [JsonIgnore]
        public new Id<EnsureConnection> Id => base.Id.Value;

        public required Id<ServiceClientCreated> ServiceClientId { get; init; }

        public required Id<InvokeRemoteProper> InvokeRemoteProper { get; init; }

        public required string Config { get; init; }

        public required ClientTransport ClientTransport { get; init; }

        Id? Is<Modifier>.Of => ServiceClientId;

        Id? Is<Effect>.Of => InvokeRemoteProper;
    }
}
