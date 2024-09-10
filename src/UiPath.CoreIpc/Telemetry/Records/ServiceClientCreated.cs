using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record ServiceClientCreated : RecordBase, IOperationStart, Is<Modifier>, IExternallyTriggered
    {
        [JsonIgnore]
        public new Id<ServiceClientCreated> Id => base.Id.Value;

        public required Id Modified { get; init; }
        public required ServiceClientKind ServiceClientKind { get; init; }
        public required string InterfaceTypeName { get; init; }
        public required string? CallbackServerConfig { get; init; }

        Id? Is<Modifier>.Of => Modified;
    }
}
