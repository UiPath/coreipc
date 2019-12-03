using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    public abstract class Listener
    {
        protected Listener(IServiceProvider serviceProvider)
        {
            Logger = serviceProvider.GetRequiredService<ILogger<Listener>>();
        }
        protected ILogger Logger { get; }
        public TaskScheduler Scheduler { get; internal set; }
        public int ConcurrentAccepts { get; private set; }
        public IDictionary<string, ServiceEndpoint> Endpoints { get; set; }
        public Task ListenAsync(CancellationToken token) =>
            Task.WhenAll(Enumerable.Range(1, ConcurrentAccepts).Select(async _ =>
            {
                while (!token.IsCancellationRequested)
                {
                    await AcceptConnection(token);
                }
            }));
        protected abstract Task AcceptConnection(CancellationToken token);
        class ServerConnection : ICreateCallback
        {
            private readonly ServiceEndpoint _serviceEndpoint;
            private readonly Connection _connection;
            private readonly Server _server;
            public ServerConnection(ServiceEndpoint serviceEndpoint, Stream network, Func<ServerConnection, IClient> clientFactory, CancellationToken cancellationToken)
            {
                _serviceEndpoint = serviceEndpoint;
                var stream = Settings.EncryptAndSign ? new NegotiateStream(network) : network;
                _connection = new Connection(stream, Logger, _serviceEndpoint.Name, serviceEndpoint.MaxMessageSize);
                _server = new Server(_serviceEndpoint, _connection, cancellationToken, new Lazy<IClient>(() => clientFactory(this)));
                Listen().LogException(Logger, serviceEndpoint.Name);
                return;
                async Task Listen()
                {
                    if (Settings.EncryptAndSign)
                    {
                        await AuthenticateAsServer();
                    }
                    await _connection.Listen();
                    return;
                    async Task AuthenticateAsServer()
                    {
                        var negotiateStream = (NegotiateStream)_connection.Network;
                        try
                        {
                            await negotiateStream.AuthenticateAsServerAsync();
                        }
                        catch
                        {
                            _connection.Dispose();
                            throw;
                        }
                        Debug.Assert(negotiateStream.IsEncrypted && negotiateStream.IsSigned);
                    }
                }
            }
            public ILogger Logger => _serviceEndpoint.Logger;
            public EndpointSettings Settings => _serviceEndpoint.Settings;
            TCallbackContract ICreateCallback.GetCallback<TCallbackContract>()
            {
                var configuredCallbackContract = Settings.CallbackContract;
                if (configuredCallbackContract == null || !typeof(TCallbackContract).IsAssignableFrom(configuredCallbackContract))
                {
                    throw new ArgumentException($"Callback contract mismatch. Requested {typeof(TCallbackContract)}, but it's {configuredCallbackContract?.ToString() ?? "not configured"}.");
                }
                Logger.LogInformation($"Create callback {_serviceEndpoint.Name}");
                var serializer = _serviceEndpoint.ServiceProvider.GetRequiredService<ISerializer>();
                return new ServiceClient<TCallbackContract>(serializer, Settings.RequestTimeout, Logger, (_, __) => Task.FromResult(_connection)).CreateProxy();
            }
        }
        protected void HandleConnection(Stream network, Func<ICreateCallback, IClient> clientFactory, CancellationToken cancellationToken) => new ServerConnection(Endpoints, network, clientFactory, cancellationToken);
    }
}