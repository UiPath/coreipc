using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public record VoidSucceeded : RecordBase, IOperationEnd, Is<Success>
    {
        public required Id StartId { get; init; }

        Id? Is<Success>.Of => StartId;
    }

    public record ResultSucceeded : RecordBase, IOperationEnd, Is<Success>
    {
        public required Id StartId { get; init; }
        public object? Result { get; init; }

        Id? Is<Success>.Of => StartId;
    }

    public sealed record DeserializationSucceeded : ResultSucceeded
    {
        [JsonIgnore]
        public new Id<DeserializationSucceeded> Id => base.Id.Value;
    }
}
