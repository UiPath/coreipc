namespace UiPath.Ipc.Extensibility;

public interface IListenerConfig<TSelf, TListenerState, TConnectionState>
    where TSelf : ListenerConfig, IListenerConfig<TSelf, TListenerState, TConnectionState>
    where TListenerState : IAsyncDisposable
{
    TListenerState CreateListenerState(IpcServer server);
    TConnectionState CreateConnectionState(IpcServer server, TListenerState listenerState);
    ValueTask<Network> AwaitConnection(TListenerState listenerState, TConnectionState connectionState, CancellationToken ct);
    IEnumerable<string> Validate();
}

