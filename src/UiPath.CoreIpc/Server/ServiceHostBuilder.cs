namespace UiPath.Ipc;

using System;

public record EndpointSettings
{
    internal TaskScheduler? Scheduler { get; set; }
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

    public void Validate()
    {
        Validator.Validate(Service.Type);
        if (Service.MaybeGetInstance() is { } instance && !instance.GetType().IsAssignableTo(Service.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(instance));
        }
    }

    protected internal virtual EndpointSettings WithServiceProvider(IServiceProvider serviceProvider)
    => new EndpointSettings(Service.Type, serviceProvider)
    {
        BeforeCall = BeforeCall,
        Scheduler = Scheduler
    };
}

public record EndpointSettings<TContract> : EndpointSettings where TContract : class
{
    public EndpointSettings(TContract? serviceInstance = null) : base(typeof(TContract), serviceInstance) { }
    public EndpointSettings(IServiceProvider serviceProvider) : base(typeof(TContract), serviceProvider) { }

    protected internal override EndpointSettings WithServiceProvider(IServiceProvider serviceProvider)
    => new EndpointSettings<TContract>(serviceProvider)
    {
        BeforeCall = BeforeCall,
        Scheduler = Scheduler
    };
}
