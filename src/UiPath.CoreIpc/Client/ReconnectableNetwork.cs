namespace UiPath.Ipc;

internal sealed class ReconnectableNetwork
{
    private readonly ClientBase _client;
    private readonly FastAsyncLock _lock = new();

    private ConnectionFactory? _userConnectionFactory;
    private Network? _network;

    public ReconnectableNetwork(ClientBase client) => _client = client;

    public async Task UpdateConnectionFactory(ConnectionFactory? userConnectionFactory, CancellationToken ct)
    {
        using (await _lock.Lock(ct))
        {
            _userConnectionFactory = userConnectionFactory;
        }
    }

    public async Task<(Network network, bool newlyConnected)> EnsureConnected(CancellationToken ct = default)
    {
        using (await _lock.Lock(ct))
        {
            var original = _network;

            // we only use the user provided network if we don't have one already
            // the user benefits from being notified about the need for a network to exist so they can prepare stuff
            // and they should reason that because a network already exists,
            // which they are presented with via the parameter, returning another one will have no effect.
            _network ??= (await (_userConnectionFactory?.Invoke(_network, ct) ?? Task.FromResult<Network?>(null)));

            var resultNetwork = _network ??= await _client.Connect(ct);
            var newlyConnected = original is null || original.Value != resultNetwork;

            return (resultNetwork, newlyConnected);
        }
    }

    private static readonly Task<Network?> CachedNullNetworkTask = Task.FromResult<Network?>(null);
}
