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
        private readonly string _pipeName;
        private readonly bool _allowImpersonation;

        public NamedPipeClient(ISerializer serializer, string pipeName, TimeSpan requestTimeout, bool allowImpersonation, ILogger logger, ConnectionFactory connectionFactory, BeforeCallHandler beforeCall, ServiceEndpoint serviceEndpoint) : base(serializer, requestTimeout, logger, connectionFactory, beforeCall, serviceEndpoint)
        {
            _pipeName = pipeName;
            _allowImpersonation = allowImpersonation;
        }

        public override string Name => base.Name ?? _pipeName;

        protected override async Task<bool> ConnectToServerAsync(CancellationToken cancellationToken)
        {
            if (_connection != null)
            {
                if (((NamedPipeClientStream)_connection.Network).IsConnected)
                {
                    return false;
                }
                _connection.Dispose();
            }
            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, _allowImpersonation ? TokenImpersonationLevel.Impersonation : TokenImpersonationLevel.Identification);
            CreateConnection(pipe);
            await pipe.ConnectAsync(cancellationToken);
            _connection.Listen().LogException(_logger, _pipeName);
            return true;
        }
    }
}