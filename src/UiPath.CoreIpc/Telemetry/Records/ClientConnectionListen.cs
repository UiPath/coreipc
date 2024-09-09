using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record ClientConnectionListen : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<ClientConnectionListen> Id => base.Id.Value;

        public required Id<Connect> Cause { get; init; }

        Id? Is<Effect>.Of => Cause;

        Id IOperationStart.Id => Id;
    }

    public sealed record EnsureConnection : RecordBase, IOperationStart, Is<Modifier>, Is<Effect>
    {
        [JsonIgnore]
        public new Id<EnsureConnection> Id => base.Id.Value;

        public required Id<ServiceClientCreated> ServiceClientId { get; init; }

        public required Id<InvokeRemoteProper> InvokeRemoteProper { get; init; }

        public required string Config { get; init; }

        public required ClientTransport ClientTransport { get; init; }

        Id? Is<Modifier>.Of => ServiceClientId;

        Id? Is<Effect>.Of => InvokeRemoteProper;
    }

    public sealed record EnsureConnectionSucceeded : RecordBase, Is<Success>
    {
        public required Id<EnsureConnection> EnsureConnectionId { get; init; }

        public required string ConnectionDebugName { get; init; }

        public required bool NewlyCreated { get; init; }

        Id? Is<Success>.Of => EnsureConnectionId;
    }

    public sealed record EnsureConnectionInitialState : RecordBase, Is<Effect>
    {
        [JsonIgnore]
        public new Id<EnsureConnectionInitialState> Id => base.Id.Value;
        public required Id<EnsureConnection> Cause { get; init; }

        public required bool HaveConnectionAlready { get; init; }
        public required bool IsConnected { get; init; }
        public required bool BeforeConnectIsNotNull { get; init; }

        Id? Is<Effect>.Of => Cause;
    }

    public sealed record Connect : RecordBase, IOperationStart, Is<Effect>
    {
        [JsonIgnore]
        public new Id<Connect> Id => base.Id.Value;
        public required Id<EnsureConnectionInitialState> Cause { get; init; }

        Id? Is<Effect>.Of => Cause;

        Id IOperationStart.Id => Id;
    }
}
