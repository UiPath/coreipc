namespace UiPath.Ipc;

internal static class ServerTransportRunner
{
    public static async Task<IAsyncDisposable> Start(ServerTransport transport)
    {
        var serverState = transport.CreateServerState();
        return serverState;
    }
}
