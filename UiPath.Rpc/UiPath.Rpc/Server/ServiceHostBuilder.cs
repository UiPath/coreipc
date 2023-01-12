namespace UiPath.Rpc;
public class ServiceHostBuilder
{
    private readonly List<Listener> _listeners = new();
    public ServiceHostBuilder(IServiceProvider serviceProvider) => ServiceProvider = serviceProvider;
    internal IServiceProvider ServiceProvider { get; }
    internal Dictionary<string, EndpointSettings> Endpoints { get; } = new();
    internal ServiceHostBuilder AddListener(Listener listener)
    {
        listener.Settings.SetValues(ServiceProvider, Endpoints);
        _listeners.Add(listener);
        return this;
    }
    public ServiceHostBuilder AddEndpointSettings(EndpointSettings settings)
    {
        settings.ServiceProvider = ServiceProvider;
        Endpoints.Add(settings.Name, settings);
        return this;
    }
    public ServiceHostBuilder AddEndpoints(IEnumerable<EndpointSettings> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            AddEndpointSettings(endpoint);
        }
        return this;
    }
    public ServiceHostBuilder AddEndpoint<TContract>(TContract serviceInstance = null) where TContract : class =>
        AddEndpointSettings(new EndpointSettings<TContract>(serviceInstance));
    public ServiceHostBuilder AddEndpoint<TContract, TCallbackContract>(TContract serviceInstance = null) where TContract : class where TCallbackContract : class =>
        AddEndpointSettings(new EndpointSettings<TContract, TCallbackContract>(serviceInstance));
    public ServiceHost Build() => new(_listeners, Endpoints);
}
public class EndpointSettings
{
    public EndpointSettings(Type contract, object serviceInstance = null, Type callbackContract = null)
    {
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        Name = contract.Name;
        ServiceInstance = serviceInstance;
        CallbackContract = callbackContract;
    }
    internal string Name { get; }
    internal TaskScheduler Scheduler { get ; set; }
    internal object ServiceInstance { get; }
    internal Type Contract { get; }
    internal Type CallbackContract { get; }
    internal IServiceProvider ServiceProvider { get; set; }
    public void Validate() => Validator.Validate(Contract, CallbackContract);
    internal object ServerObject() => ServiceInstance ?? ServiceProvider.GetRequiredService(Contract);
}
public class EndpointSettings<TContract> : EndpointSettings where TContract : class
{
    public EndpointSettings(TContract serviceInstance = null, Type callbackContract = null) : base(typeof(TContract), serviceInstance, callbackContract) { }
}
public class EndpointSettings<TContract, TCallbackContract> : EndpointSettings<TContract> where TContract : class where TCallbackContract : class
{
    public EndpointSettings(TContract serviceInstance = null) : base(serviceInstance, typeof(TCallbackContract)) { }
}
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRpc(this IServiceCollection services) => services;
}