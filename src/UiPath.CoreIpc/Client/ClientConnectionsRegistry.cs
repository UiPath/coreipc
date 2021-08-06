using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    static class ClientConnectionsRegistry
    {
        private static readonly ConcurrentDictionaryWrapper<IConnectionKey, ClientConnection> _connections = new(CreateClientConnection);
        public static async Task<ClientConnectionHandle> GetOrCreate(IConnectionKey key, CancellationToken cancellationToken)
        {
            var clientConnection = GetOrAdd(key);
            var asyncLock = await clientConnection.Lock(cancellationToken);
            try
            {
                // check again just in case it was removed after GetOrAdd but before entering the lock
                ClientConnection newClientConnection;
                while ((newClientConnection = GetOrAdd(key)) != clientConnection)
                {
                    asyncLock.Dispose();
                    asyncLock = await newClientConnection.Lock(cancellationToken);
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
        private static ClientConnection GetOrAdd(IConnectionKey key)=>_connections.GetOrAdd(key);
        static ClientConnection CreateClientConnection(IConnectionKey key) => key.CreateClientConnection(key);
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
        ClientConnection CreateClientConnection(IConnectionKey key);
    }
    abstract class ClientConnection : IDisposable
    {
        readonly AsyncLock _lock = new();
        Connection _connection;
        protected ClientConnection(IConnectionKey connectionKey) => ConnectionKey = connectionKey;
        public abstract bool Connected { get; }
        public abstract Stream Network { get; }
        public Connection Connection
        {
            get => _connection;
            set
            {
                _connection = value;
                _connection.Closed += OnConnectionClosed;
            }
        }
        public abstract Task Connect(CancellationToken cancellationToken);
        private void OnConnectionClosed(object sender, EventArgs _)
        {
            var closedConnection = (Connection)sender;
            if (!ClientConnectionsRegistry.TryGet(ConnectionKey, out var clientConnection) || clientConnection.Connection != closedConnection)
            {
                return;
            }
            if (!clientConnection.TryLock(out var guard))
            {
                return;
            }
            using (guard)
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
        protected IConnectionKey ConnectionKey { get; }
        public Task<IDisposable> Lock(CancellationToken cancellationToken = default) => _lock.LockAsync(cancellationToken);
        public bool TryLock(out IDisposable guard)
        {
            try
            {
                guard = _lock.Lock(new(canceled: true));
                return true;
            }
            catch (TaskCanceledException)
            {
                guard = null;
                return false;
            }
        }
        public override string ToString() => _connection?.ToString() ?? base.ToString();
        protected virtual void Dispose(bool disposing) {}
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}