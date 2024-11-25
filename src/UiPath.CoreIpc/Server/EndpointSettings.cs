namespace UiPath.Ipc;

using System;

public record EndpointSettings
{
    public TaskScheduler? Scheduler { get; set; }
    public BeforeCallHandler? BeforeIncommingCall { get; set; }
    public Type ContractType => Service.Type;
    public object? ServiceInstance => Service.MaybeGetInstance();
    public IServiceProvider? ServiceProvider => Service.MaybeGetServiceProvider();
    internal ServiceFactory Service { get; }

    public EndpointSettings(Type contractType, object? serviceInstance = null) : this(
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

    public virtual EndpointSettings WithServiceProvider(IServiceProvider? serviceProvider)
    => new(Service.WithProvider(serviceProvider));

    public void Validate()
    {
        Validator.Validate(Service.Type);
        if (Service.MaybeGetInstance() is { } instance && !Service.Type.IsAssignableFrom(instance.GetType()))
        {
            throw new ArgumentOutOfRangeException(nameof(instance));
        }
    }
}

public sealed record EndpointSettings<TContract> : EndpointSettings where TContract : class
{
    public EndpointSettings(TContract? serviceInstance = null) : base(typeof(TContract), serviceInstance) { }
    public EndpointSettings(IServiceProvider serviceProvider) : base(typeof(TContract), serviceProvider) { }
    private EndpointSettings(ServiceFactory service) : base(service) { }

    public override EndpointSettings WithServiceProvider(IServiceProvider? serviceProvider)
    => new EndpointSettings<TContract>(Service.WithProvider(serviceProvider));
}
