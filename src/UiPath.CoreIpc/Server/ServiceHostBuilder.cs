namespace UiPath.Ipc;

using System;
public class ServiceHostBuilder
{
    private readonly List<Listener> _listeners = new();
    internal IServiceProvider ServiceProvider { get; }
    internal Dictionary<string, EndpointSettings> Endpoints { get; } = new();
    private readonly HashSet<Type> _allowedCallbacks = new();

    public ServiceHostBuilder(IServiceProvider serviceProvider)
    => ServiceProvider = serviceProvider;

    public ServiceHostBuilder AddEndpoint(EndpointSettings settings)
    {
        Endpoints.Add(settings.Name, settings);
        return this;
    }
    public ServiceHostBuilder AllowCallback(Type callbackType)
    {
        _ = _allowedCallbacks.Add(callbackType);
        return this;
    }
    internal ServiceHostBuilder AddListener(Listener listener)
    {
        listener.Settings.RouterConfig = new(Endpoints);
        _listeners.Add(listener);
        return this;
    }
    public ServiceHost Build() => new(ServiceProvider, _listeners, Endpoints);
}
public static class ServiceHostBuilderExtensions
{
    public static ServiceHostBuilder AddEndpoints(this ServiceHostBuilder serviceHostBuilder, IEnumerable<EndpointSettings> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            serviceHostBuilder.AddEndpoint(endpoint);
        }
        return serviceHostBuilder;
    }
    public static ServiceHostBuilder AddEndpoint<TContract>(this ServiceHostBuilder serviceHostBuilder, TContract? serviceInstance = null) where TContract : class =>
        serviceHostBuilder.AddEndpoint(new EndpointSettings<TContract>(serviceInstance));
}
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIpc(this IServiceCollection services)
    {
        services.AddSingleton<ISerializer, IpcJsonSerializer>();
        return services;
    }
}

public class EndpointSettings
{
    private TaskScheduler? _scheduler;
    internal void SetScheduler(TaskScheduler? scheduler) => _scheduler = scheduler;
    internal TaskScheduler Scheduler => _scheduler ?? TaskScheduler.Default;

    public BeforeCallHandler? BeforeCall { get; set; }
    internal ServiceFactory Service { get; }

    internal string Name => Service.Type.Name;

    public EndpointSettings(Type contractType, object? serviceInstance) : this(
        serviceInstance is not null
            ? new ServiceFactory.Instance()
            {
                Type = contractType ?? throw new ArgumentNullException(nameof(contractType)),
                ServiceInstance = serviceInstance
            }
            : new ServiceFactory.Deferred()
            {
                Type = contractType ?? throw new ArgumentNullException(nameof(contractType)),
            })
    { }

    public EndpointSettings(Type contractType, IServiceProvider serviceProvider) : this(
        new ServiceFactory.Injected()
        {
            Type = contractType ?? throw new ArgumentNullException(nameof(contractType)),
            ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider))
        })
    { }

    private protected EndpointSettings(ServiceFactory service) => Service = service;

    public void Validate() => Validator.Validate(Service.Type);
}

public class EndpointSettings<TContract> : EndpointSettings where TContract : class
{
    public EndpointSettings(TContract? serviceInstance = null) : base(typeof(TContract), serviceInstance) { }
    public EndpointSettings(IServiceProvider serviceProvider) : base(typeof(TContract), serviceProvider) { }
}
