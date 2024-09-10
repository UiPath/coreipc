using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record ServerConnectionDisposed : VoidSucceeded
    {
        [JsonIgnore]
        public new Id<ServerConnectionDisposed> Id => base.Id.Value;
    }
}
