using Microsoft.Extensions.Logging.Abstractions;

namespace UiPath.Ipc;

internal readonly record struct RouterConfig(IReadOnlyDictionary<string, EndpointSettings> Endpoints);

internal readonly struct Router
{
    public static readonly Router Callbacks = default;

    private readonly IServiceProvider? _serviceProvider;
    private readonly RouterConfig? _config;

    public Router(IServiceProvider serviceProvider, RouterConfig config)
    {
        _serviceProvider = serviceProvider;
        _config = config;
    }

    public bool TryResolve(string endpoint, out Route route)
    {
        if (_config is null)
        {
            return Callback.TryResolveRoute(endpoint, out route);
        }

        if (_serviceProvider is null)
        {
            throw new InvalidOperationException();
        }

        if (!_config.Value.Endpoints.TryGetValue(endpoint, out var endpointSettings))
        {
            route = default;
            return false;
        }

        route = Route.From(_serviceProvider!, endpointSettings);
        return true;
    }
}

internal abstract record ServiceFactory
{
    public required Type Type { get; init; }

    public abstract IDisposable? Get(out object service);

    public virtual ServiceFactory WithProvider(IServiceProvider serviceProvider) => this;

    public sealed record Injected : ServiceFactory
    {
        public required IServiceProvider ServiceProvider { get; init; }

        public override IDisposable? Get(out object service)
        {
            var scope = ServiceProvider.CreateScope();
            service = scope.ServiceProvider.GetRequiredService(Type);
            return scope;
        }

        public override ServiceFactory WithProvider(IServiceProvider serviceProvider)
        => this with { ServiceProvider = serviceProvider };
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

        public override ServiceFactory WithProvider(IServiceProvider serviceProvider)
        => new Injected()
        {
            Type = Type,
            ServiceProvider = serviceProvider
        };
    }
}

internal readonly struct Route
{
    public static Route From(IServiceProvider serviceProvider, EndpointSettings endpointSettings)
    => new Route()
    {
        Service = endpointSettings.Service.WithProvider(serviceProvider),
        BeforeCall = endpointSettings.BeforeCall,
        Scheduler = endpointSettings.Scheduler,
        LoggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>,
        Serializer = serviceProvider.GetRequiredService<ISerializer>
    };

    public static Route From(Callback.CallbackRegistration callbackRegistration)
    => new()
    {
        Service = callbackRegistration.Service,
        BeforeCall = null,
        Scheduler = callbackRegistration.Scheduler ?? TaskScheduler.Default,
        LoggerFactory = () => callbackRegistration.Logger ?? NullLoggerFactory.Instance,
        Serializer = () => callbackRegistration.Serializer
    };

    public required ServiceFactory Service { get; init; }
    public required BeforeCallHandler? BeforeCall { get; init; }
    public required TaskScheduler Scheduler { get; init; }
    public Func<ILoggerFactory> LoggerFactory { get; init; }
    public Func<ISerializer?> Serializer { get; init; }

    public Task MaybeBeforeCall(CallInfo callInfo, CancellationToken ct)
    => BeforeCall?.Invoke(callInfo, ct) ?? Task.CompletedTask;
}
