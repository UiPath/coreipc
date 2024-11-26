namespace UiPath.Ipc;

internal interface IClientState : IDisposable
{
    Stream? Network { get; }

    bool IsConnected();
    ValueTask Connect(IpcClient client, CancellationToken ct);
}
