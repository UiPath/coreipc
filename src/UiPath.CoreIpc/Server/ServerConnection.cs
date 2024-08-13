using System.IO.Pipes;
using System.Net.Security;
namespace UiPath.Ipc;

public interface IClient
{
    TCallbackInterface GetCallback<TCallbackInterface>() where TCallbackInterface : class;
    void Impersonate(Action action);
}

internal sealed class ServerConnection<TConfig, TListenerState, TConnectionState> : ServerConnection
    where TConfig : ListenerConfig, IListenerConfig<TConfig, TListenerState, TConnectionState>
    where TListenerState : IAsyncDisposable
{
    public new readonly Listener<TConfig, TListenerState, TConnectionState> Listener;

    public ServerConnection(Listener<TConfig, TListenerState, TConnectionState> listener) : base(listener)
    {
        Listener = listener;
    }

    public override async Task<Stream> AcceptClient(CancellationToken ct)
    => (await Listener.Config.AwaitConnection(
        Listener.State,
        Listener.Config.CreateConnectionState(
            Listener.Server,
            Listener.State),
        ct))
        .AsStream();
}

internal abstract class ServerConnection : IClient, IDisposable
{
    public readonly Listener Listener;

    private readonly ConcurrentDictionary<Type, object> _callbacks = new();
    internal Connection? Connection;
    private Task<Connection>? _connectionAsTask;
    private Server? Server;

    protected ServerConnection(Listener listener)
    {
        Listener = listener;
    }

    protected internal virtual void Initialize() { }

    public abstract Task<Stream> AcceptClient(CancellationToken cancellationToken);

    public void Impersonate(Action action)
    {
        if (Connection is null)
        {
            throw new InvalidOperationException("The server connection is not listening yet.");
        }

        if (Connection.Network is not NamedPipeServerStream pipeStream)
        {
            action();
            return;
        }

        pipeStream.RunAsClient(() => action());
    }

    TCallbackInterface IClient.GetCallback<TCallbackInterface>() where TCallbackInterface : class
    {
        return (TCallbackInterface)_callbacks.GetOrAdd(typeof(TCallbackInterface), CreateCallback);

        TCallbackInterface CreateCallback(Type callbackContract)
        {
            Listener.Logger.LogInformation($"Create callback {callbackContract} {Listener.Config.DebugName}");

            _connectionAsTask ??= Task.FromResult(Connection!);

            // TODO: rethink this double specification of TCallbackInterface
            return new ServiceClientForCallback(Connection!, Listener, typeof(TCallbackInterface)).GetProxy<TCallbackInterface>();
        }
    }
    public async Task Listen(Stream network, CancellationToken cancellationToken)
    {
        var stream = await AuthenticateAsServer(); // TODO: should we decommission this?
        var serializer = Listener.Server.ServiceProvider.GetService<ISerializer>();
        Connection = new Connection(stream, serializer, Listener.Logger, debugName: Listener.ToString(), maxMessageSize: Listener.Config.MaxMessageSize);
        Server = new Server(
            new Router(
                Listener.Config.CreateRouterConfig(Listener.Server),
                Listener.Server.ServiceProvider),
            Listener.Config.RequestTimeout,
            Connection,
            client: this);

        // close the connection when the service host closes
        using (cancellationToken.UnsafeRegister(_ => Connection.Dispose(), null!))
        {
            await Connection.Listen();
        }
        return;

        // TODO: should we decommission this?
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
