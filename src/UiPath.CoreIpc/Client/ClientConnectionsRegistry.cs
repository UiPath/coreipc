﻿using Microsoft.Extensions.Logging;
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
        public static ClientConnection GetOrCreate(IConnectionKey key) => _connections.GetOrAdd(key, localKey=>new ClientConnection(localKey));
        public static bool TryGet(IConnectionKey key, out ClientConnection connection) => _connections.TryGetValue(key, out connection);
        public static void Clear()
        {
            foreach (var connection in _connections.Values)
            {
                connection.Close();
            }
        }
        public static void Close(IConnectionKey key)
        {
            if (TryGet(key, out var connection))
            {
                connection.Close();
            }
        }
        internal static bool Remove(IConnectionKey connectionKey) => _connections.TryRemove(connectionKey, out _);
    }
    public interface IConnectionKey : IEquatable<IConnectionKey>
    {
    }
    class ClientConnection
    {
        AsyncLock _lock = new AsyncLock();
        Connection _connection;
        public ClientConnection(IConnectionKey connectionKey) => ConnectionKey = connectionKey;
        public Stream Network { get; set; }
        public Connection Connection
        {
            get => _connection;
            set
            {
                _connection = value;
                _connection.Closed += (_, __) => OnConnectionClosed().LogException(_connection.Logger, _connection);
            }
        }
        private async Task OnConnectionClosed()
        {
            if (!ClientConnectionsRegistry.TryGet(ConnectionKey, out var clientConnection) || clientConnection.Connection != Connection)
            {
                return;
            }
            using (await clientConnection.LockAsync())
            {
                if (clientConnection.Connection != Connection)
                {
                    return;
                }
                ClientConnectionsRegistry.Remove(ConnectionKey);
                _connection.Logger.LogInformation($"Remove connection {_connection}.");
            }
        }
        public Server Server { get; set; }
        IConnectionKey ConnectionKey { get; }
        public Task<IDisposable> LockAsync(CancellationToken cancellationToken = default) => _lock.LockAsync(cancellationToken);
        public void Close()
        {
            Connection?.Dispose();
            Server?.Endpoints.Clear();
        }
    }
}