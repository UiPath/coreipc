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

    internal class NamedPipeClient<TInterface> : ServiceClient<TInterface> where TInterface : class
    {
        private readonly string _serverName;
        private readonly string _pipeName;
        private readonly bool _allowImpersonation;
        private NamedPipeClientStream _pipe;

        public NamedPipeClient(string serverName, string pipeName, ISerializer serializer, TimeSpan requestTimeout, bool allowImpersonation, ILogger logger, ConnectionFactory connectionFactory, bool encryptAndSign, BeforeCallHandler beforeCall, EndpointSettings serviceEndpoint) : base(serializer, requestTimeout, logger, connectionFactory, encryptAndSign, beforeCall, serviceEndpoint)
        {
            _serverName = serverName;
            _pipeName = pipeName;
            _allowImpersonation = allowImpersonation;
        }

        public override string Name => base.Name ?? _pipeName;

        protected override async Task<bool> ConnectToServerAsync(CancellationToken cancellationToken)
        {
            if (_pipe != null)
            {
                if (_pipe.IsConnected)
                {
                    return false;
                }
                _pipe.Dispose();
            }
            _pipe = new NamedPipeClientStream(_serverName, _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, _allowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);
            try
            {
                await _pipe.ConnectAsync(cancellationToken);
            }
            catch
            {
                _pipe.Dispose();
                throw;
            }
            await CreateConnection(_pipe, _pipeName);
            _connection.Listen().LogException(_logger, _pipeName);
            return true;
        }
    }
}