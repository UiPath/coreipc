using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record InvokeRemoteProper : RecordBase, IOperationStart, Is<SubOperation>
    {
        [JsonIgnore]
        public new Id<InvokeRemoteProper> Id => base.Id.Value;
        public required Id<InvokeRemote> InvokeRemoteId { get; init; }

        public required TimeSpan ClientTimeout { get; init; }
        public required TimeSpan MessageTimeout { get; init; }

        public required string[] SerializedArgs { get; init; }

        public override string ToString()
        => $"{base.ToString()} {nameof(SerializedArgs)}: [{string.Join(",", SerializedArgs)}]";

        Id IOperationStart.Id => Id;
        Id? Is<SubOperation>.Of => InvokeRemoteId;
    }
}
