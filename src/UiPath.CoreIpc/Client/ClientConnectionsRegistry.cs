using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    static class ClientConnectionsRegistry
    {
        private static readonly ConcurrentDictionary<IConnectionKey, ClientConnection> _connections = new();
        public static async Task<ClientConnectionHandle> GetOrCreate(IConnectionKey key, CancellationToken cancellationToken)
        {
            var clientConnection = GetOrAdd(key);
            var asyncLock = await clientConnection.LockAsync(cancellationToken);
            try
            {
                // check again just in case it was removed after GetOrAdd but before entering the lock
                ClientConnection newClientConnection;
                while ((newClientConnection = GetOrAdd(key)) != clientConnection)
                {
                    asyncLock.Dispose();
                    asyncLock = await newClientConnection.LockAsync(cancellationToken);
                    clientConnection = newClientConnection;
                }
            }
            catch
            {
                asyncLock.Dispose();
                throw;
            }
            return new(clientConnection, asyncLock);
        }
        private static ClientConnection GetOrAdd(IConnectionKey key)=>_connections.GetOrAdd(key, localKey => new(localKey));
        public static bool TryGet(IConnectionKey key, out ClientConnection connection) => _connections.TryGetValue(key, out connection);
        internal static ClientConnection Remove(IConnectionKey connectionKey)
        {
            _connections.TryRemove(connectionKey, out var clientConnection);
            return clientConnection;
        }
    }
    readonly struct ClientConnectionHandle : IDisposable
    {
        private readonly IDisposable _asyncLock;
        public ClientConnectionHandle(ClientConnection clientConnection, IDisposable asyncLock)
        {
            ClientConnection = clientConnection;
            _asyncLock = asyncLock;
        }
        public ClientConnection ClientConnection { get; }
        public void Dispose() => _asyncLock.Dispose();
    }
    interface IConnectionKey : IEquatable<IConnectionKey>
    {
        bool EncryptAndSign { get; }
    }
    class ClientConnection
    {
        readonly AsyncLock _lock = new();
        Connection _connection;
        public ClientConnection(IConnectionKey connectionKey) => ConnectionKey = connectionKey;
        public object State { get; set; }
        public Connection Connection
        {
            get => _connection;
            set
            {
                _connection = value;
                _connection.Closed += delegate { OnConnectionClosed(_connection).LogException(_connection.Logger, _connection); };
            }
        }
        private async Task OnConnectionClosed(Connection closedConnection)
        {
            if (!ClientConnectionsRegistry.TryGet(ConnectionKey, out var clientConnection) || clientConnection.Connection != closedConnection)
            {
                return;
            }
            using (await clientConnection.LockAsync())
            {
                if (!ClientConnectionsRegistry.TryGet(ConnectionKey, out clientConnection) || clientConnection.Connection != closedConnection)
                {
                    return;
                }
                var removedConnection = ClientConnectionsRegistry.Remove(ConnectionKey);
                _connection.Logger?.LogInformation($"Remove connection {removedConnection}.");
                Debug.Assert(removedConnection?.Connection == closedConnection, "Removed the wrong connection.");
            }
        }
        public Server Server { get; set; }
        IConnectionKey ConnectionKey { get; }
        public Task<IDisposable> LockAsync(CancellationToken cancellationToken = default) => _lock.LockAsync(cancellationToken);
        public void Close() => Connection?.Dispose();
        public override string ToString() => _connection?.ToString() ?? base.ToString();
    }
}