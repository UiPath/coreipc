using System.IO.Pipes;

namespace UiPath.Ipc;

internal sealed class ServerConnection : IClient, IDisposable, IServiceClientConfig
{
    public static void CreateAndListen(IpcServer server, Stream network, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            _ = new ServerConnection(server, await server.Transport.MaybeAuthenticate(network), ct);
        });
    }

    private readonly string _debugName;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<Type, object> _callbacks = new();
    private readonly IpcServer _ipcServer;

    private readonly Stream _network;
    private readonly Connection _connection;
    private readonly Server _server;

    private readonly Task _listening;

    private ServerConnection(IpcServer server, Stream network, CancellationToken ct)
    {
        _ipcServer = server;

        _debugName = $"{nameof(ServerConnection)} {RuntimeHelpers.GetHashCode(this)}";
        _logger = server.CreateLogger(_debugName);

        _network = network;

        _connection = new Connection(network, _debugName, _logger, maxMessageSize: _ipcServer.Transport.MaxMessageSize);
        _server = new Server(new Router(_ipcServer), _ipcServer.RequestTimeout, _connection, client: this);

        _listening = Listen(ct);
    }

    private async Task Listen(CancellationToken ct)
    {
        // close the connection when the service host closes
        using (ct.UnsafeRegister(_ => _connection.Dispose(), state: null))
        {
            await _connection.Listen();
        }
    }

    void IDisposable.Dispose() => _network.Dispose();

    TCallbackInterface IClient.GetCallback<TCallbackInterface>()
    {
        return (TCallbackInterface)_callbacks.GetOrAdd(typeof(TCallbackInterface), CreateCallback);

        TCallbackInterface CreateCallback(Type callbackContract)
        {
            _logger.LogInformation($"Create callback {callbackContract}.");
            return new ServiceClientForCallback<TCallbackInterface>(_connection, config: this).GetProxy();
        }
    }
    void IClient.Impersonate(Action action)
    {
        if (_connection.Network is not NamedPipeServerStream pipeStream)
        {
            action();
            return;
        }

        pipeStream.RunAsClient(() => action());
    }

    #region IServiceClientConfig 
    TimeSpan IServiceClientConfig.RequestTimeout => _ipcServer.RequestTimeout;
    BeforeConnectHandler? IServiceClientConfig.BeforeConnect => null;
    BeforeCallHandler? IServiceClientConfig.BeforeCall => null;
    ILogger? IServiceClientConfig.Logger => _logger;
    string IServiceClientConfig.DebugName => _debugName;
    #endregion
}
