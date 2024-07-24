using System.Diagnostics.CodeAnalysis;

namespace UiPath.Ipc.Extensibility;

// TODO: Rethink this class.
public abstract class ClientConnection : IDisposable
{
    private readonly SemaphoreSlim _lock = new(initialCount: 1);
    private Connection? _connection;

    public abstract bool Connected { get; }

    internal Connection? Connection => _connection;

    internal Server? Server { get; private set; }

    [MemberNotNull(nameof(_connection))]
    [MemberNotNull(nameof(Connection))]
    [MemberNotNullWhen(returnValue: true, nameof(Server))]
    internal bool Initialize(Connection connection, Server? server)
    {
        if (connection != _connection)
        {
            if (_connection is not null)
            {
                _connection.Closed -= OnConnectionClosed;
            }
            _connection = connection;

            Server = server;
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

    internal protected virtual void Initialize() { }
}

public abstract class ClientConnection<TConnectionKey> : ClientConnection where TConnectionKey : ConnectionKey
{
    public new TConnectionKey ConnectionKey => (base.ConnectionKey as TConnectionKey)!;
}