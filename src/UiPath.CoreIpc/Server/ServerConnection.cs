using System.Net.Security;
namespace UiPath.Ipc;

public interface IClient
{
    TCallbackInterface GetCallback<TCallbackInterface>() where TCallbackInterface : class;
    void Impersonate(Action action);
}

public abstract class ServerConnection<TListener> : ServerConnection where TListener : Listener
{
    public new TListener Listener => (base.Listener as TListener)!;
}

public abstract class ServerConnection : IClient, IDisposable
{
    internal Listener Listener { get; set; } = null!;
    public ListenerConfig Config => Listener.Config;

    private readonly ConcurrentDictionary<Type, object> _callbacks = new();
    internal Connection? Connection;
    private Task<Connection>? _connectionAsTask;
    private Server? Server;

    protected internal virtual void Initialize() { }

    public abstract Task<Stream> AcceptClient(CancellationToken cancellationToken);

    public virtual void Impersonate(Action action) => action();

    TCallbackInterface IClient.GetCallback<TCallbackInterface>() where TCallbackInterface : class
    {
        return (TCallbackInterface)_callbacks.GetOrAdd(typeof(TCallbackInterface), CreateCallback);

        TCallbackInterface CreateCallback(Type callbackContract)
        {
            Listener.Log($"Create callback {callbackContract} {Listener.DebugName}");

            _connectionAsTask ??= Task.FromResult(Connection!);

            var serviceClient = new ServiceClient<TCallbackInterface>(new ConnectionConfig()
            {
                ConnectionFactory = (_, _) => _connectionAsTask,
                ServiceProvider = Listener.Server.Config.ServiceProvider,
                RequestTimeout = Listener.Config.RequestTimeout,
                Logger = Listener.Logger,
            });
            return serviceClient.CreateProxy();
        }
    }
    public async Task Listen(Stream network, CancellationToken cancellationToken)
    {
        var stream = await AuthenticateAsServer();
        var serializer = Listener.Server.Config.ServiceProvider.GetService<ISerializer>();
        Connection = new Connection(stream, serializer, Listener.Logger, Listener.DebugName, Listener.MaxMessageSize);
        Server = new Server(
            new Router(Listener.Config.CreateRouterConfig(), Listener.Server.Config.ServiceProvider),
            Listener.Config.RequestTimeout, Connection, client: this);

        // close the connection when the service host closes
        using (cancellationToken.UnsafeRegister(_ => Connection.Dispose(), null!))
        {
            await Connection.Listen();
        }
        return;

        async Task<Stream> AuthenticateAsServer()
        {
            if (Listener.Config.Certificate is null)
            {
                return network;
            }

            var sslStream = new SslStream(network);
            try
            {
                await sslStream.AuthenticateAsServerAsync(Listener.Config.Certificate);
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