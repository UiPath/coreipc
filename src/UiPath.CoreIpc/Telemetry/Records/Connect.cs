using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record Connect : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<Connect> Id => base.Id.Value;
        public required Id<EnsureConnectionInitialState> Cause { get; init; }

        Id? Is<Effect>.Of => Cause;

        Id IOperationStart.Id => Id;
    }
}
