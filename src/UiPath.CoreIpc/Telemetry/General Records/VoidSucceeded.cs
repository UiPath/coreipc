using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public partial record VoidSucceeded : RecordBase, IOperationEnd, Is<Success>
    {
        public required Id StartId { get; init; }

        Id? Is<Success>.Of => StartId;
    }
}
