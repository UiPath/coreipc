namespace UiPath.Ipc;

internal readonly record struct RouterConfig(IReadOnlyDictionary<string, EndpointSettings> Endpoints)
{
    public static RouterConfig From(EndpointCollection endpoints, Func<EndpointSettings, EndpointSettings> transform)
    {
        ContractToSettingsMap nameToEndpoint = [];

        foreach (var endpoint in endpoints)
        {
            var newEndpoint = transform(endpoint);
            foreach (var iface in endpoint.Service.Type.GetInterfaces().Prepend(endpoint.Service.Type))
            {
                nameToEndpoint[iface.Name] = newEndpoint;
            }
        }

        return new(nameToEndpoint);
    }
}

internal readonly struct Router
{
    private readonly RouterConfig? _config; // nullable for the case when the constructor is bypassed
    private readonly IServiceProvider? _serviceProvider;

    public Router(IpcServer ipcServer)
    {
        _config = ipcServer.CreateRouterConfig(ipcServer);
        _serviceProvider = ipcServer.ServiceProvider;
    }

    public Router(RouterConfig config, IServiceProvider? serviceProvider)
    {
        _config = config;
        _serviceProvider = serviceProvider;
    }

    public bool TryResolve(string endpoint, out Route route)
    {
        if (_config is not { } config) /// in case <see cref="Router"/> was allocated as <c>default(Router)</c>, bypassing the constructor
        {
            throw new InvalidOperationException();
        }

        if (config.Endpoints.TryGetValue(endpoint, out var endpointSettings))
        {
            route = Route.From(_serviceProvider, endpointSettings);
            return true;
        }

        route = default;
        return false;
    }
}

internal abstract record ServiceFactory
{
    public required Type Type { get; init; }

    public abstract IDisposable? Get(out object service);

    public virtual ServiceFactory WithProvider(IServiceProvider? serviceProvider) => this;

    internal virtual object? MaybeGetInstance() => null;
    internal virtual IServiceProvider? MaybeGetServiceProvider() => null;

    public sealed record Injected : ServiceFactory
    {
        public required IServiceProvider ServiceProvider { get; init; }

        internal override IServiceProvider? MaybeGetServiceProvider() => ServiceProvider;

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

        internal override object? MaybeGetInstance() => ServiceInstance;

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
        Scheduler = endpointSettings.Scheduler.OrDefault(),
        LoggerFactory = serviceProvider.MaybeCreateServiceFactory<ILoggerFactory>(),
    };

    public required ServiceFactory Service { get; init; }

    public TaskScheduler Scheduler { get; init; }
    public BeforeCallHandler? BeforeCall { get; init; }
    public Func<ILoggerFactory>? LoggerFactory { get; init; }
}
