namespace UiPath.Ipc;

partial class Telemetry
{
    public abstract record HonorDeserialization : RecordBase, IOperationStart, Is<Effect>
    {
        public required Id<DeserializationSucceeded> Cause { get; init; }

        public HonorDeserialization(
            string memberName,
            string filePath,
            int line,
            string stackTrace) : base(memberName, filePath, line, stackTrace) { }

        Id? Is<Effect>.Of => Cause;
    }
}
