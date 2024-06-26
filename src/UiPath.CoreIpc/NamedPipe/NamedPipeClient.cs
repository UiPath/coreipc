using System.IO.Pipes;
using System.Security.Principal;

namespace UiPath.Ipc.NamedPipe;

using ConnectionFactory = Func<Connection, CancellationToken, Task<Connection>>;
using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;

interface INamedPipeKey : IConnectionKey
{
    string ServerName { get; }
    string PipeName { get; }
    bool AllowImpersonation { get; }
}

class NamedPipeClient<TInterface> : ServiceClient<TInterface>, INamedPipeKey where TInterface : class
{
    public NamedPipeClient(string serverName, string pipeName, ISerializer serializer, TimeSpan requestTimeout, bool allowImpersonation, ILogger logger, ConnectionFactory connectionFactory, BeforeCallHandler beforeCall, EndpointSettings serviceEndpoint)
        : base(serializer, requestTimeout, logger, connectionFactory, beforeCall, serviceEndpoint)
    {
        ServerName = serverName;
        PipeName = pipeName;
        AllowImpersonation = allowImpersonation;
        HashCode = (serverName, pipeName, allowImpersonation).GetHashCode();
    }
    public override string Name => base.Name ?? PipeName;
    public string ServerName { get; }
    public string PipeName { get; }
    public bool AllowImpersonation { get; }
    public override bool Equals(IConnectionKey other) => other == this || (other is INamedPipeKey otherClient &&
        otherClient.ServerName == ServerName && otherClient.PipeName == PipeName && otherClient.AllowImpersonation == AllowImpersonation && base.Equals(other));
    public override ClientConnection CreateClientConnection() => new NamedPipeClientConnection(this);
    class NamedPipeClientConnection : ClientConnection
    {
        private NamedPipeClientStream _pipe;
        public NamedPipeClientConnection(IConnectionKey connectionKey) : base(connectionKey) { }
        public override bool Connected => _pipe?.IsConnected is true;
        protected override void Dispose(bool disposing)
        {
            _pipe?.Dispose();
            base.Dispose(disposing);
        }
        public override async Task<Stream> Connect(CancellationToken cancellationToken)
        {
            var key = (INamedPipeKey)ConnectionKey;
            _pipe = new(key.ServerName, key.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, key.AllowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);
            await _pipe.ConnectAsync(cancellationToken);
            return _pipe;
        }
    }
}