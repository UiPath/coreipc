using System.Diagnostics.CodeAnalysis;

namespace UiPath.Ipc;

static class ClientConnectionsRegistry
{
    private static readonly ConcurrentDictionary<ConnectionKey, ClientConnection> Connections = new();

    public static async Task<ClientConnection> GetOrCreate(ConnectionKey key, CancellationToken cancellationToken)
    {
        var clientConnection = GetOrAdd(key);
        await clientConnection.Lock(cancellationToken);
        try
        {
            // check again just in case it was removed after GetOrAdd but before entering the lock
            ClientConnection newClientConnection;
            while ((newClientConnection = GetOrAdd(key)) != clientConnection)
            {
                clientConnection.Release();
                await newClientConnection.Lock(cancellationToken);
                clientConnection = newClientConnection;
            }
        }
        catch
        {
            clientConnection.Release();
            throw;
        }
        return clientConnection;
    }

    private static ClientConnection GetOrAdd(ConnectionKey key) => Connections.GetOrAdd(key, key => key.CreateClientConnection());

    public static bool TryGet(ConnectionKey key, [NotNullWhen(returnValue: true)] out ClientConnection? connection) => Connections.TryGetValue(key, out connection);

    internal static ClientConnection? Remove(ConnectionKey connectionKey)
    {
        _ = Connections.TryRemove(connectionKey, out var clientConnection);
        return clientConnection;
    }
}
