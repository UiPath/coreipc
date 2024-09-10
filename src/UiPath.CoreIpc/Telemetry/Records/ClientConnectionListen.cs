using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record ClientConnectionListen : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<ClientConnectionListen> Id => base.Id.Value;

        public required Id<Connect> Cause { get; init; }

        Id? Is<Effect>.Of => Cause;

        Id IOperationStart.Id => Id;
    }
}
