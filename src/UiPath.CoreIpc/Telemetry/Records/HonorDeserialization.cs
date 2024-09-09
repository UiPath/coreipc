namespace UiPath.Ipc;

partial class Telemetry
{
    public abstract record HonorDeserialization : RecordBase, IOperationStart, Is<Effect>
    {
        public required Id<DeserializationSucceeded> Cause { get; init; }

        Id? Is<Effect>.Of => Cause;
    }
}
