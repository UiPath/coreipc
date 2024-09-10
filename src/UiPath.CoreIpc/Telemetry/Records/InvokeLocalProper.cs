using Newtonsoft.Json;
namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record InvokeLocalProper : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<InvokeLocalProper> Id => base.Id.Value;
        public required Id<InvokeLocal> InvokeMethodId { get; init; }

        Id? Is<Effect>.Of => InvokeMethodId;
    }
}
