namespace UiPath.CoreIpc;

static class ClientConnectionsRegistry
{
    private static readonly ConcurrentDictionary<IConnectionKey, ClientConnection> Connections = new();
    public static async Task<ClientConnection> GetOrCreate(IConnectionKey key, CancellationToken cancellationToken)
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
    private static ClientConnection GetOrAdd(IConnectionKey key) => Connections.GetOrAdd(key, key => key.CreateClientConnection());
    public static bool TryGet(IConnectionKey key, out ClientConnection connection) => Connections.TryGetValue(key, out connection);
    internal static ClientConnection Remove(IConnectionKey connectionKey)
    {
        Connections.TryRemove(connectionKey, out var clientConnection);
        return clientConnection;
    }
}
interface IConnectionKey : IEquatable<IConnectionKey>
{
    string SslServer { get; }
    ClientConnection CreateClientConnection();
}
abstract class ClientConnection : IDisposable
{
    readonly SemaphoreSlim _lock = new(1);
    Connection _connection;
    protected ClientConnection(IConnectionKey connectionKey) => ConnectionKey = connectionKey;
    public abstract bool Connected { get; }
    public Connection Connection
    {
        get => _connection;
        set
        {
            _connection = value;
            _connection.Closed += OnConnectionClosed;
        }
    }
    public abstract Task<Stream> Connect(CancellationToken cancellationToken);
    private void OnConnectionClosed(object sender, EventArgs _)
    {
        var closedConnection = (Connection)sender;
        if (!ClientConnectionsRegistry.TryGet(ConnectionKey, out var clientConnection) || clientConnection.Connection != closedConnection)
        {
            return;
        }
        if (!clientConnection.TryLock())
        {
            return;
        }
        try
        {
            if (!ClientConnectionsRegistry.TryGet(ConnectionKey, out clientConnection) || clientConnection.Connection != closedConnection)
            {
                return;
            }
            var removedConnection = ClientConnectionsRegistry.Remove(ConnectionKey);
            if (_connection.LogEnabled)
            {
                _connection.Log($"Remove connection {removedConnection}.");
            }
            Debug.Assert(removedConnection?.Connection == closedConnection, "Removed the wrong connection.");
        }
        finally
        {
            Release();
        }
    }
    protected IConnectionKey ConnectionKey { get; }
    public Task Lock(CancellationToken cancellationToken = default) => _lock.WaitAsync(cancellationToken);
    public void Release() => _lock.Release();
    public bool TryLock() => _lock.Wait(millisecondsTimeout: 0);
    public override string ToString() => _connection?.Name ?? base.ToString();
    protected virtual void Dispose(bool disposing) => _lock.AssertDisposed();
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}