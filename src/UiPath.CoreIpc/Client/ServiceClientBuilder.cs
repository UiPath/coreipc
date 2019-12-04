using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Security;

namespace UiPath.CoreIpc
{
    using ConnectionFactory = Func<Connection, CancellationToken, Task<Connection>>;
    using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;

    public abstract class ServiceClientBuilder<TDerived, TInterface> where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
    {
        protected readonly IServiceProvider _serviceProvider;
        protected readonly Type _callbackContract;
        protected ISerializer _serializer = new JsonSerializer();
        protected TimeSpan _requestTimeout = Timeout.InfiniteTimeSpan;
        protected ILogger _logger;
        protected ConnectionFactory _connectionFactory;
        protected BeforeCallHandler _beforeCall;
        protected object _callbackInstance;
        protected TaskScheduler _taskScheduler;
        protected bool _encryptAndSign;

        protected ServiceClientBuilder(Type callbackContract, IServiceProvider serviceProvider)
        {
            IOHelpers.Validate(typeof(TInterface));
            _callbackContract = callbackContract;
            _serviceProvider = serviceProvider;
        }

        public TDerived DontReconnect() => ConnectionFactory((connection, _) => Task.FromResult(connection));

        public TDerived ConnectionFactory(ConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
            return (TDerived)this;
        }

        public TDerived EncryptAndSign()
        {
            _encryptAndSign = true;
            return (TDerived)this;
        }

        public TDerived BeforeCall(BeforeCallHandler beforeCall)
        {
            _beforeCall = beforeCall;
            return (TDerived)this;
        }

        public TDerived Logger(ILogger logger)
        {
            _logger = logger;
            return (TDerived)this;
        }

        public TDerived Logger(IServiceProvider serviceProvider) => Logger(serviceProvider.GetRequiredService<ILogger<TInterface>>());

        public TDerived Serializer(ISerializer serializer)
        {
            _serializer = serializer;
            return (TDerived) this;
        }

        public TDerived RequestTimeout(TimeSpan timeout)
        {
            _requestTimeout = timeout;
            return (TDerived) this;
        }

        protected abstract TInterface BuildCore(ServiceEndpoint serviceEndpoint);

        public TInterface Build()
        {
            if (_callbackContract == null)
            {
                return BuildCore(null);
            }
            if (_logger == null)
            {
                Logger(_serviceProvider);
            }
            var endpointSettings = new EndpointSettings(_callbackContract, _callbackInstance);
            return BuildCore(new ServiceEndpoint(_serviceProvider, endpointSettings, _logger) { Scheduler = _taskScheduler });
        }
    }

    public readonly struct CallInfo
    {
        public CallInfo(bool newConnection) => NewConnection = newConnection;
        public bool NewConnection { get; }
    }
}