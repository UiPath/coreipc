namespace UiPath.Ipc.Extensibility;

internal interface IListenerConfig<TSelf, TListenerState, TConnectionState>
    where TSelf : ServerTransport, IListenerConfig<TSelf, TListenerState, TConnectionState>
    where TListenerState : IAsyncDisposable
{
    TListenerState CreateListenerState(IpcServer server);
    TConnectionState CreateConnectionState(IpcServer server, TListenerState listenerState);
    ValueTask<Stream> AwaitConnection(TListenerState listenerState, TConnectionState connectionState, CancellationToken ct);
    IEnumerable<string> Validate();
}
