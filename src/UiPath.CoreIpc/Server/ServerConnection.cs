using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
namespace UiPath.CoreIpc
{
    public interface IClient
    {
        TCallbackInterface GetCallback<TCallbackInterface>(EndpointSettings endpoint) where TCallbackInterface : class;
        void Impersonate(Action action);
    }
    abstract class ServerConnection : IClient, IDisposable
    {
        private readonly ConcurrentDictionary<EndpointSettings, object> _callbacks = new();
        protected readonly Listener _listener;
        private Connection _connection;
        private Server _server;
        protected ServerConnection(Listener listener) => _listener = listener;
        public ILogger Logger => _listener.Logger;
        public ListenerSettings Settings => _listener.Settings;
        public abstract Task AcceptClient(CancellationToken cancellationToken);
        protected abstract Stream Network { get; }
        public virtual void Impersonate(Action action) => action();
        public TCallbackInterface GetCallback<TCallbackInterface>(EndpointSettings endpoint) where TCallbackInterface : class =>
            (TCallbackInterface)_callbacks.GetOrAdd(endpoint, CreateCallback<TCallbackInterface>);
        TCallbackContract CreateCallback<TCallbackContract>(EndpointSettings endpoint) where TCallbackContract : class
        {
            var configuredCallbackContract = endpoint.CallbackContract;
            if (configuredCallbackContract == null || !typeof(TCallbackContract).IsAssignableFrom(configuredCallbackContract))
            {
                throw new ArgumentException($"Callback contract mismatch. Requested {typeof(TCallbackContract)}, but it's {configuredCallbackContract?.ToString() ?? "not configured"}.");
            }
            Logger.LogInformation($"Create callback {_listener.Name}");
            var serviceClient = new ServiceClient<TCallbackContract>(_connection.Serializer, Settings.RequestTimeout, Logger, (_,_) => Task.FromResult(_connection));
            return serviceClient.CreateProxy();
        }
        public async Task Listen(CancellationToken cancellationToken)
        {
            var stream = await AuthenticateAsServer();
            var serializer = Settings.ServiceProvider.GetRequiredService<ISerializer>();
            _connection = new(stream, serializer, Logger, _listener.Name, _listener.MaxMessageSize);
            _server = new(Settings, _connection, this, cancellationToken);
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