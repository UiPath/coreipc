namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record InstantiateConnection : RecordBase, Is<Effect>
    {
        public required Id<ServerConnectionListen> ServerConnectionListenId { get; init; }

        Id? Is<Effect>.Of => ServerConnectionListenId;
    }
}
