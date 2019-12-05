using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.NamedPipe
{
    static class NamedPipeRegistry
    {
        private static readonly ConcurrentDictionary<IConnectionKey, Task<ClientConnection>> _connections = new ConcurrentDictionary<IConnectionKey, Task<ClientConnection>>();

        public static Task<ClientConnection> GetOrCreate(IConnectionKey key, Func<IConnectionKey, Task<ClientConnection>> factory) => _connections.GetOrAdd(key, factory);
    }

    public interface IConnectionKey : IEquatable<IConnectionKey>
    {
    }

    class ClientConnection
    {
    }
}