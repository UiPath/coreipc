namespace UiPath.Ipc.Transport.WebSocket;

public sealed class WebSocketServerTransport : ServerTransport
{
    public required Accept Accept { get; init; }

    internal override IServerState CreateServerState() => new State { Transport = this };

    internal override IEnumerable<string?> ValidateCore()
    {
        yield return IsNotNull(Accept);
    }

    public override string ToString() => nameof(WebSocketServerTransport);

    private sealed class State : IServerState, IServerConnectionSlot
    {
        public required WebSocketServerTransport Transport { get; init; }

        public async ValueTask<Stream> AwaitConnection(CancellationToken ct)
        {
            var webSocket = await Transport.Accept(ct);
            return new WebSocketStream(webSocket);
        }

        public IServerConnectionSlot CreateConnectionSlot() => this;

        public void Dispose() { }
        public ValueTask DisposeAsync() => default;
    }
}
