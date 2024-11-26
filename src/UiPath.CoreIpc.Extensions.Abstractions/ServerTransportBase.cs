using UiPath.Ipc;

namespace UiPath.CoreIpc.Extensions.Abstractions;

public abstract class ServerTransportBase : ServerTransport
{
    protected abstract ServerState CreateState();
    protected new abstract IEnumerable<string?> Validate();

    internal override IServerState CreateServerState() => CreateState();
    internal override IEnumerable<string?> ValidateCore() => Validate();
}
public abstract class ServerState : ServerTransport.IServerState
{
    public abstract ValueTask DisposeAsync();
    public abstract ServerConnectionSlot CreateServerConnectionSlot();

    ServerTransport.IServerConnectionSlot ServerTransport.IServerState.CreateConnectionSlot() => CreateServerConnectionSlot();
}

public abstract class ServerConnectionSlot : ServerTransport.IServerConnectionSlot
{
    public abstract ValueTask<Stream> AwaitConnection(CancellationToken ct);

    public abstract ValueTask DisposeAsync();
}
