namespace UiPath.Ipc;

public interface IClientState : IDisposable
{
    Stream? Network { get; }

    bool IsConnected();
    ValueTask Connect(IpcClient client, CancellationToken ct);
}
