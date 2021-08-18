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
        TCallbackInterface GetCallback<TCallbackInterface>(Type callbackContract) where TCallbackInterface : class;
        void Impersonate(Action action);
    }
    abstract class ServerConnection : IClient, IDisposable
    {
        private readonly ConcurrentDictionary<Type, object> _callbacks = new();
        protected readonly Listener _listener;
        private Connection _connection;
        private Server _server;
        protected ServerConnection(Listener listener) => _listener = listener;
        public ILogger Logger => _listener.Logger;
        public ListenerSettings Settings => _listener.Settings;
        public abstract Task AcceptClient(CancellationToken cancellationToken);
        protected abstract Stream Network { get; }
        public virtual void Impersonate(Action action) => action();
        TCallbackInterface IClient.GetCallback<TCallbackInterface>(Type callbackContract) where TCallbackInterface : class
        {
            if (callbackContract == null)
            {
                throw new InvalidOperationException($"Callback contract mismatch. Requested {typeof(TCallbackInterface)}, but it's not configured.");
            }
            return (TCallbackInterface)_callbacks.GetOrAdd(callbackContract, CreateCallback);
            TCallbackInterface CreateCallback(Type callbackContract)
            {
                if (!typeof(TCallbackInterface).IsAssignableFrom(callbackContract))
                {
                    throw new ArgumentException($"Callback contract mismatch. Requested {typeof(TCallbackInterface)}, but it's {callbackContract}.");
                }
                Logger.LogInformation($"Create callback {_listener.Name}");
                var serviceClient = new ServiceClient<TCallbackInterface>(_connection.Serializer, Settings.RequestTimeout, Logger, (_, _) => Task.FromResult(_connection));
                return serviceClient.CreateProxy();
            }
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