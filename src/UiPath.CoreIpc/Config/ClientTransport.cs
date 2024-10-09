namespace UiPath.Ipc;

public abstract record ClientTransport
{
    public abstract IClientState CreateState();
    public abstract void Validate();
}
