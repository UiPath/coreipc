using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record EnsureConnectionInitialState : RecordBase, Is<Effect>
    {
        [JsonIgnore]
        public new Id<EnsureConnectionInitialState> Id => base.Id.Value;
        public required Id<EnsureConnection> Cause { get; init; }

        public required bool HaveConnectionAlready { get; init; }
        public required bool IsConnected { get; init; }
        public required bool BeforeConnectIsNotNull { get; init; }

        Id? Is<Effect>.Of => Cause;
    }
}
