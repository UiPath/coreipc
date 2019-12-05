using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    static class ClientConnectionsRegistry
    {
        private static readonly ConcurrentDictionary<IConnectionKey, ClientConnection> _connections = new ConcurrentDictionary<IConnectionKey, ClientConnection>();
        public static ClientConnection GetOrCreate(IConnectionKey key) => _connections.GetOrAdd(key, _=>new ClientConnection());
        public static bool TryGet(IConnectionKey key, out ClientConnection connection) => _connections.TryGetValue(key, out connection);
        public static void Clear()
        {
            foreach (var connection in _connections.Values)
            {
                connection.Close();
            }
        }
    }
    public interface IConnectionKey : IEquatable<IConnectionKey>
    {
    }
    class ClientConnection
    {
        AsyncLock _lock = new AsyncLock();
        public Stream Network { get; set; }
        public Connection Connection { get; set; }
        public Server Server { get; set; }
        internal Task<IDisposable> LockAsync(CancellationToken cancellationToken) => _lock.LockAsync(cancellationToken);
        public void Close()
        {
            Connection?.Dispose();
            Server?.Endpoints.Clear();
        }
    }
}