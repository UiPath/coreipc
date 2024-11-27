namespace UiPath.Ipc;

public abstract record ClientTransport
{
    private protected ClientTransport() { }

    internal abstract IClientState CreateState();

    internal abstract void Validate();
}
