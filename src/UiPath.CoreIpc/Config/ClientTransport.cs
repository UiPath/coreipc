namespace UiPath.Ipc;

public abstract record ClientTransport
{
    internal abstract IClientState CreateState();
    internal abstract void Validate();
}
