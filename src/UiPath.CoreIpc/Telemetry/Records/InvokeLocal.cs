using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record InvokeLocal : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<InvokeLocal> Id => base.Id.Value;
        public required Id<HandleRequest> HandleRequestId { get; init; }
        public required bool RouteSchedulerIsNotNull { get; init; }
        public required bool RouteSchedulerIsDefault { get; init; }
        public required string ReturnTaskTypeName { get; init; }
        public required bool ReturnTaskTypeIsGenericType { get; init; }

        Id? Is<Effect>.Of => HandleRequestId;
    }

    public enum ServiceClientKind
    {
        Proper,
        Callback
    }
}
