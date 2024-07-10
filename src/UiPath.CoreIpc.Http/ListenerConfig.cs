using UiPath.Ipc;

namespace UiPath.CoreIpc.Http;

partial class BidirectionalHttp
{
    public sealed partial record ListenerConfig : ListenerConfig<ListenerState, ServerConnectionState>
    {
        public required Uri Uri { get; init; }

        protected override ListenerState CreateListenerState(IpcServer server) => new ListenerState(server, this);

        protected override async ValueTask<IAsyncStream> AwaitConnection(ListenerState listener, CancellationToken ct)
        => await listener.NewConnections.ReadAsync(ct);
    }
}

