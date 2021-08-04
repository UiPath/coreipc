using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    abstract class ServerConnection : ICreateCallback, IDisposable
    {
        protected readonly Listener _listener;
        private Connection _connection;
        private Server _server;
        protected ServerConnection(Listener listener) => _listener = listener;
        public ILogger Logger => _listener.Logger;
        public ListenerSettings Settings => _listener.Settings;
        public abstract Task AcceptClient(CancellationToken cancellationToken);
        protected abstract Stream Network { get; }
        protected virtual void Impersonate(Action action) => action(); 
        TCallbackContract ICreateCallback.GetCallback<TCallbackContract>(EndpointSettings endpoint)
        {
            var configuredCallbackContract = endpoint.CallbackContract;
            if (configuredCallbackContract == null || !typeof(TCallbackContract).IsAssignableFrom(configuredCallbackContract))
            {
                throw new ArgumentException($"Callback contract mismatch. Requested {typeof(TCallbackContract)}, but it's {configuredCallbackContract?.ToString() ?? "not configured"}.");
            }
            Logger.LogInformation($"Create callback {_listener.Name}");
            var serializer = _listener.ServiceProvider.GetRequiredService<ISerializer>();
            var serviceClient = new ServiceClient<TCallbackContract>(serializer, Settings.RequestTimeout, Logger, (_, __) => Task.FromResult(_connection));
            return serviceClient.CreateProxy();
        }
        Client CreateClient() => new(Impersonate, this);
        public async Task Listen(CancellationToken cancellationToken)
        {
            var stream = await AuthenticateAsServer();
            var serializer = Settings.ServiceProvider.GetRequiredService<ISerializer>();
            _connection = new(stream, serializer, Logger, _listener.Name, _listener.MaxMessageSize);
            _server = new(Logger, Settings, _connection, new(CreateClient), cancellationToken);
            // close the connection when the service host closes
            using (cancellationToken.Register(_connection.Dispose))
            {
                await _connection.Listen();
            }
            return;
            async Task<Stream> AuthenticateAsServer()
            {
                if (!Settings.EncryptAndSign)
                {
                    return Network;
                }
                var negotiateStream = new NegotiateStream(Network);
                try
                {
                    await negotiateStream.AuthenticateAsServerAsync();
                }
                catch
                {
                    negotiateStream.Dispose();
                    throw;
                }
                Debug.Assert(negotiateStream.IsEncrypted && negotiateStream.IsSigned);
                return negotiateStream;
            }
        }
        protected virtual void Dispose(bool disposing){}
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}