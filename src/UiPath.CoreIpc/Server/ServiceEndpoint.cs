using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Security;
using System.Diagnostics;

namespace UiPath.CoreIpc
{
    public class ServiceEndpoint
    {
        protected readonly int _maxMessageSize;
        private TaskScheduler _scheduler;
        internal ServiceEndpoint(IServiceProvider serviceProvider, EndpointSettings settings, ILogger logger)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _maxMessageSize = Settings.MaxReceivedMessageSizeInMegabytes * 1024 * 1024;
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ILogger Logger { get; }
        internal EndpointSettings Settings { get; }
        public string Name => Settings.Name;
        public IServiceProvider ServiceProvider { get; }
        public TaskScheduler Scheduler { get => _scheduler; set => _scheduler = value ?? TaskScheduler.Default; }

        public Task ListenAsync(CancellationToken token) =>
            Task.WhenAll(Enumerable.Range(1, Settings.ConcurrentAccepts).Select(async _ =>
            {
                while (!token.IsCancellationRequested)
                {
                    await AcceptConnection(token);
                }
            }));

        protected virtual Task AcceptConnection(CancellationToken token) => throw new NotImplementedException();

        class ServerConnection : ICreateCallback
        {
            private readonly ServiceEndpoint _serviceEndpoint;
            private readonly Connection _connection;
            private readonly Server _server;
            public ServerConnection(ServiceEndpoint serviceEndpoint, Stream network, Func<ServerConnection, IClient> clientFactory, CancellationToken cancellationToken)
            {
                _serviceEndpoint = serviceEndpoint;
                var stream = Settings.EncryptAndSign ? new NegotiateStream(network) : network;
                _connection = new Connection(stream, Logger, _serviceEndpoint.Name, serviceEndpoint._maxMessageSize);
                _server = new Server(_serviceEndpoint, _connection, cancellationToken, new Lazy<IClient>(() => clientFactory(this)));
                Listen().LogException(Logger, serviceEndpoint.Name);
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
                return new ServiceClient<TCallbackContract>(_serviceEndpoint.ServiceProvider.GetRequiredService<ISerializer>(), Settings.RequestTimeout, Logger, (_, __) => Task.FromResult(_connection)).CreateProxy();
            }
            public async Task Listen()
            {
                if (Settings.EncryptAndSign)
                {
                    var negotiateStream = (NegotiateStream)_connection.Network;
                    await negotiateStream.AuthenticateAsServerAsync();
                    Debug.Assert(negotiateStream.IsEncrypted && negotiateStream.IsSigned);
                }
                await _connection.Listen();
            }
        }

        protected void HandleConnection(Stream network, Func<ICreateCallback, IClient> clientFactory, CancellationToken cancellationToken) => new ServerConnection(this, network, clientFactory, cancellationToken);
    }
}