using System.Collections.Concurrent;
using UiPath.Ipc;

namespace Playground;

internal static class Impl
{
    public sealed class ClientRegistry
    {
        private readonly ConcurrentDictionary<ClientPair, object?> _clients = new();

        public bool Add(ClientPair pair) => _clients.TryAdd(pair, value: null);

        public IReadOnlyList<ClientPair> All() => _clients.Keys.ToArray();
    }

    public readonly record struct ClientPair(Contracts.IClientOperations Client, Contracts.IClientOperations2 Client2);

    public sealed class Server(ClientRegistry clients) : Contracts.IServerOperations
    {
        public async Task<bool> Register(Message? m = null)
        {
            var clientOps = m!.GetCallback<Contracts.IClientOperations>();
            var clientOps2 = m.GetCallback<Contracts.IClientOperations2>();

            var added = clients.Add(new(clientOps, clientOps2));

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
            var pairs = clients.All();

            foreach (var pair in pairs)
            {
                var time = await pair.Client2.GetTheTime();
                _ = await pair.Client.Greet($"{text} - You said the time was: {time}");
            }

            return true;
        }
    }

    public sealed class ClientOperations() : Contracts.IClientOperations
    {
        public async Task<bool> Greet(string text)
        {
            Console.WriteLine($"Scheduler: {TaskScheduler.Current.GetType().Name}");
            Console.WriteLine($"Server says: {text}");
            return true;
        }
    }

    public sealed class Client2 : Contracts.IClientOperations2
    {
        public Task<DateTime> GetTheTime() => Task.FromResult(DateTime.Now);
    }
}
