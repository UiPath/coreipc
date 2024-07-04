using System.Collections.Concurrent;
using UiPath.Ipc;

namespace Playground;

internal static class Impl
{
    public sealed class Server : Contracts.IServerOperations
    {
        private readonly ConcurrentDictionary<Contracts.IClientOperations, object?> _clients = new();

        public async Task<bool> Register(Message? m = null)
        {
            var client = m!.GetCallback<Contracts.IClientOperations>();
            var added = _clients.TryAdd(client, value: null);

            if (added)
            {
                Console.WriteLine("New client registered.");
            }
            else
            {
                Console.WriteLine("Client tried to register again resulting in a NOP.");
            }

            return true;
        }

        public async Task<bool> Broadcast(string text)
        {
            var clients = _clients.Keys.ToArray();

            foreach (var client in clients)
            {
                _ = await client.Greet(text);
            }

            return true;
        }
    }

    public sealed class Client(Func<string, Task<bool>> greet) : Contracts.IClientOperations
    {
        public Task<bool> Greet(string text) => greet(text);
    }

    public sealed class Client2 : Contracts.IClientOperations2
    {
        public Task<DateTime> GetTheTime() => Task.FromResult(DateTime.Now);
    }
}
