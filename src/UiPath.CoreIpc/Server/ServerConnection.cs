using System.Net.Security;
namespace UiPath.Ipc;

public interface IClient
{
    TCallbackInterface GetCallback<TCallbackInterface>() where TCallbackInterface : class;
    void Impersonate(Action action);
}

internal abstract class ServerConnection : IClient, IDisposable
{
    private readonly ConcurrentDictionary<Type, object> _callbacks = new();
    protected readonly Listener _listener;
    private Connection? _connection;
    private Task<Connection>? _connectionAsTask;
    private Server? _server;

    protected ServerConnection(Listener listener) => _listener = listener;

    public abstract Task<Stream> AcceptClient(CancellationToken cancellationToken);

    public virtual void Impersonate(Action action) => action();

    TCallbackInterface IClient.GetCallback<TCallbackInterface>() where TCallbackInterface : class
    {
        return (TCallbackInterface)_callbacks.GetOrAdd(typeof(TCallbackInterface), CreateCallback);

        TCallbackInterface CreateCallback(Type callbackContract)
        {
            _listener.Log($"Create callback {callbackContract} {_listener.DebugName}");

            _connectionAsTask ??= Task.FromResult(_connection!);

            var serviceClient = new ServiceClient<TCallbackInterface>(new ConnectionConfig()
            {
                ConnectionFactory = (_, _) => _connectionAsTask,
                ServiceProvider = _listener.Server.Config.ServiceProvider,
                RequestTimeout = _listener.Config.RequestTimeout,
                Logger = _listener.Logger,
            });
            return serviceClient.CreateProxy();
        }
    }
    public async Task Listen(Stream network, CancellationToken cancellationToken)
    {
        var stream = await AuthenticateAsServer();
        var serializer = _listener.Server.Config.ServiceProvider.GetService<ISerializer>();
        _connection = new Connection(stream, serializer, _listener.Logger, _listener.DebugName, _listener.MaxMessageSize);
        _server = new Server(
            new Router(_listener.Config.CreateRouterConfig(), _listener.Server.Config.ServiceProvider),
            _listener.Config.RequestTimeout, _connection, client: this);

        // close the connection when the service host closes
        using (cancellationToken.UnsafeRegister(_ => _connection.Dispose(), null!))
        {
            await _connection.Listen();
        }
        return;

        async Task<Stream> AuthenticateAsServer()
        {
            if (_listener.Config.Certificate is null)
            {
                return network;
            }

            var sslStream = new SslStream(network);
            try
            {
                await sslStream.AuthenticateAsServerAsync(_listener.Config.Certificate);
            }
            catch
            {
                sslStream.Dispose();
                throw;
            }

            Debug.Assert(sslStream.IsEncrypted && sslStream.IsSigned);
            return sslStream;
        }
    }
    protected virtual void Dispose(bool disposing) { }
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}