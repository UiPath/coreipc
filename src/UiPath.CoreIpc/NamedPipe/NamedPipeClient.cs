using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using System.IO;

namespace UiPath.CoreIpc.NamedPipe
{
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
        public NamedPipeClient(string serverName, string pipeName, ISerializer serializer, TimeSpan requestTimeout, bool allowImpersonation, ILogger logger, ConnectionFactory connectionFactory, string sslServer, BeforeCallHandler beforeCall, EndpointSettings serviceEndpoint)
            : base(serializer, requestTimeout, logger, connectionFactory, sslServer, beforeCall, serviceEndpoint)
        {
            ServerName = serverName;
            PipeName = pipeName;
            AllowImpersonation = allowImpersonation;
            HashCode = (serverName, pipeName, allowImpersonation, sslServer).GetHashCode();
        }
        public override string Name => base.Name ?? PipeName;
        public string ServerName { get; }
        public string PipeName { get; }
        public bool AllowImpersonation { get; }
        public override bool Equals(IConnectionKey other) => other == this || (other is INamedPipeKey otherClient &&
            otherClient.ServerName == ServerName && otherClient.PipeName == PipeName && otherClient.AllowImpersonation == AllowImpersonation && base.Equals(other));
        public override ClientConnection CreateClientConnection(IConnectionKey key) => new NamedPipeClientConnection(key);
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
            public override Stream Network => _pipe;
            public override Task Connect(CancellationToken cancellationToken)
            {
                var key = (INamedPipeKey)ConnectionKey;
                _pipe = new(key.ServerName, key.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, key.AllowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);
                return _pipe.ConnectAsync(cancellationToken);
            }
        }
    }
}