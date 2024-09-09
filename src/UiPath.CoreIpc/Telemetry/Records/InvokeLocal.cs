using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed record InvokeLocal : RecordBase, IOperationStart, Is<Effect>
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

    public sealed record ServiceClientCreated : RecordBase, IOperationStart, Is<Modifier>, IExternallyTriggered
    {
        [JsonIgnore]
        public new Id<ServiceClientCreated> Id => base.Id.Value;

        public required Id Modified { get; init; }
        public required ServiceClientKind ServiceClientKind { get; init; }
        public required string InterfaceTypeName { get; init; }
        public required string? CallbackServerConfig { get; init; }

        Id? Is<Modifier>.Of => Modified;
    }

    public enum ServiceClientKind
    {
        Proper,
        Callback
    }

    public sealed record ServiceClientDisposed : VoidSucceeded;

    //public sealed record Foo : RecordBase 
    //{ 
    //    public Id<Connect>? ConnectId { get; init; }
    //    public Id<AcceptClientSucceeded>? AcceptClientSuccededId { get; init; }

    //    Id IOperationStart.Id => new UntypedId(Id);
    //    Id? Is<Effect>.Of => (ConnectId as Id) ?? AcceptClientSuccededId;
    //}

    public sealed record InvokeRemote : RecordBase, IOperationStart, Is<SubOperation>
    {
        [JsonIgnore]
        public new Id<InvokeRemote> Id => base.Id.Value;
        public required Id<ServiceClientCreated> ServiceClientId { get; init; }

        public required string Method { get; init; }
        public required bool DefaultSynchronizationContext { get; init; }

        Id IOperationStart.Id => Id;
        Id? Is<SubOperation>.Of => ServiceClientId;
    }

    public sealed record InvokeRemoteProper : RecordBase, IOperationStart, Is<SubOperation>
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
