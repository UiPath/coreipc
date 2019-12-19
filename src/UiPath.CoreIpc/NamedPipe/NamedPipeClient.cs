using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

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

    internal class NamedPipeClient<TInterface> : ServiceClient<TInterface>, INamedPipeKey where TInterface : class
    {
        private readonly int _hashCode;
        private NamedPipeClientStream _pipe;
        public NamedPipeClient(string serverName, string pipeName, ISerializer serializer, TimeSpan requestTimeout, bool allowImpersonation, ILogger logger, ConnectionFactory connectionFactory, bool encryptAndSign, BeforeCallHandler beforeCall, EndpointSettings serviceEndpoint) : base(serializer, requestTimeout, logger, connectionFactory, encryptAndSign, beforeCall, serviceEndpoint)
        {
            ServerName = serverName;
            PipeName = pipeName;
            AllowImpersonation = allowImpersonation;
            _hashCode = (serverName, pipeName, allowImpersonation, encryptAndSign).GetHashCode();
        }
        public override string Name => base.Name ?? PipeName;
        public string ServerName { get; }
        public string PipeName { get; }
        public bool AllowImpersonation { get; }
        public override int GetHashCode() => _hashCode;
        bool IEquatable<IConnectionKey>.Equals(IConnectionKey other) => other == this || (other is INamedPipeKey otherClient &&
            otherClient.ServerName == ServerName && otherClient.PipeName == PipeName && otherClient.AllowImpersonation == AllowImpersonation && otherClient.EncryptAndSign == EncryptAndSign);
        protected override async Task<bool> ConnectToServerAsync(CancellationToken cancellationToken)
        {
            if (_pipe?.IsConnected == true)
            {
                return false;
            }
            var (clientConnection, asyncLock) = await ClientConnectionsRegistry.GetOrCreate(this, cancellationToken);
            using (asyncLock)
            {
                var pipe = (NamedPipeClientStream)clientConnection.Network;
                if (pipe != null)
                {
                    if (pipe.IsConnected)
                    {
                        ReuseClientConnection(clientConnection);
                        _pipe = pipe;
                        return false;
                    }
                    pipe.Dispose();
                }
                pipe = new NamedPipeClientStream(ServerName, PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, AllowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);
                try
                {
                    await pipe.ConnectAsync(cancellationToken);
                }
                catch
                {
                    pipe.Dispose();
                    throw;
                }
                await CreateClientConnection(clientConnection, pipe, PipeName);
                _pipe = pipe;
                return true;
            }
        }
    }
}