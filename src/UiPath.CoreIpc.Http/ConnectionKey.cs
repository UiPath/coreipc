using UiPath.Ipc;

namespace UiPath.CoreIpc.Http;

partial class BidirectionalHttp
{
    public sealed record ConnectionKey : ConnectionKey<ClientConnection>
    {
        public required Uri ServerUri { get; init; }
        public required Uri ClientUri { get; init; }
    }
}

