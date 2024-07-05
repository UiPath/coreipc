namespace UiPath.Ipc;

internal readonly record struct RouterConfig(IReadOnlyDictionary<string, EndpointSettings> Endpoints);

internal readonly struct Router
{
    private readonly RouterConfig? _config; // nullable for the case when the constructor is bypassed
    private readonly IServiceProvider? _serviceProvider;

    public Router(RouterConfig config, IServiceProvider? serviceProvider)
    {
        _config = config;
        _serviceProvider = serviceProvider;
    }

    public bool TryResolve(string endpoint, out Route route)
    {
        if (_config is null) /// in case <see cref="Router"/> was allocated as default, bypassing the constructor
        {
            throw new InvalidOperationException();
        }

        if (!_config.Value.Endpoints.TryGetValue(endpoint, out var endpointSettings))
        {
            route = default;
            return false;
        }

        route = Route.From(_serviceProvider, endpointSettings);
        return true;
    }
}

internal abstract record ServiceFactory
{
    public required Type Type { get; init; }

    public abstract IDisposable? Get(out object service);

    public virtual ServiceFactory WithProvider(IServiceProvider? serviceProvider) => this;

    public sealed record Injected : ServiceFactory
    {
        public required IServiceProvider ServiceProvider { get; init; }

        public override IDisposable? Get(out object service)
        {
            var scope = ServiceProvider.CreateScope();
            service = scope.ServiceProvider.GetRequiredService(Type);
            return scope;
        }

        public override ServiceFactory WithProvider(IServiceProvider? serviceProvider)
        {
            if (serviceProvider is null)
            {
                throw new InvalidOperationException();
            }

            return this with { ServiceProvider = serviceProvider };
        }
    }

    public sealed record Instance : ServiceFactory
    {
        public required object ServiceInstance { get; init; }

        public override IDisposable? Get(out object service)
        {
            service = ServiceInstance;
            return null;
        }
    }

    public sealed record Deferred : ServiceFactory
    {
        public override IDisposable? Get(out object service)
        {
            throw new NotSupportedException();
        }

        public override ServiceFactory WithProvider(IServiceProvider? serviceProvider)
        {
            if (serviceProvider is null)
            {
                throw new InvalidOperationException();
            }

            return new Injected()
            {
                Type = Type,
                ServiceProvider = serviceProvider
            };
        }
    }
}

internal readonly struct Route
{
    public static Route From(IServiceProvider? serviceProvider, EndpointSettings endpointSettings)
    => new Route()
    {
        Service = endpointSettings.Service.WithProvider(serviceProvider),
        BeforeCall = endpointSettings.BeforeCall,
        Scheduler = endpointSettings.Scheduler,
        LoggerFactory = serviceProvider.MaybeGetFactory<ILoggerFactory>(),
        Serializer = serviceProvider.MaybeGetFactory<ISerializer>()
    };

    public required ServiceFactory Service { get; init; }

    public TaskScheduler Scheduler { get; init; }
    public BeforeCallHandler? BeforeCall { get; init; }
    public Func<ILoggerFactory>? LoggerFactory { get; init; }
    public Func<ISerializer>? Serializer { get; init; }
}
