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

// TODO: Rethink this class.
internal abstract class ClientConnection : IDisposable
{
    private readonly SemaphoreSlim _lock = new(initialCount: 1);
    private Connection? _connection;

    public abstract bool Connected { get; }

    public Connection? Connection => _connection;

    public Server? Server { get; private set; }
    protected ConnectionKey ConnectionKey { get; }

    protected ClientConnection(ConnectionKey connectionKey) => ConnectionKey = connectionKey;

    [MemberNotNull(nameof(_connection))]
    [MemberNotNull(nameof(Connection))]
    [MemberNotNullWhen(returnValue: true, nameof(Server))]
    public bool Initialize(Connection connection, Server? server)
    {
        if (connection != _connection)
        {
            if (_connection is not null)
            {
                _connection.Closed -= OnConnectionClosed;
            }
            _connection = connection;

            _connection = connection;
            _connection.Closed += OnConnectionClosed;
        }

        Server = server;
#pragma warning disable CS8774 // Member must have a non-null value when exiting.
        return server is not null;
#pragma warning restore CS8774 // Member must have a non-null value when exiting.
    }

    public abstract Task<Stream> Connect(CancellationToken cancellationToken);

    private void OnConnectionClosed(object? sender, EventArgs _)
    {
        var closedConnection = sender as Connection;

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
            if (_connection?.LogEnabled ?? false)
            {
                _connection.Logger.LogInformation($"Remove connection {removedConnection}.");
            }
            Debug.Assert(removedConnection?.Connection == closedConnection, "Removed the wrong connection.");
        }
        finally
        {
            Release();
        }
    }
    public Task Lock(CancellationToken cancellationToken = default) => _lock.WaitAsync(cancellationToken);
    public void Release() => _lock.Release();
    public bool TryLock() => _lock.Wait(millisecondsTimeout: 0);
    public override string? ToString() => _connection?.DebugName ?? base.ToString();
    protected virtual void Dispose(bool disposing) => _lock.AssertDisposed();
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}