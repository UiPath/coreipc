namespace UiPath.Ipc.Transport.WebSocket;

public sealed class WebSocketServerTransport : ServerTransport, ServerTransport.IServerState, ServerTransport.IServerConnectionSlot
{
    public required Accept Accept { get; init; }

    protected internal override IServerState CreateServerState() => this;

    IServerConnectionSlot IServerState.CreateConnectionSlot() => this;

    async ValueTask<Stream> IServerConnectionSlot.AwaitConnection(CancellationToken ct)
    {
        var webSocket = await Accept(ct);
        return new WebSocketStream(webSocket);
    }
    ValueTask IAsyncDisposable.DisposeAsync() => default;
    void IDisposable.Dispose() { }

    protected override IEnumerable<string?> ValidateCore()
    {
        yield return IsNotNull(Accept);
    }

    public override string ToString() => nameof(WebSocketServerTransport);
}
