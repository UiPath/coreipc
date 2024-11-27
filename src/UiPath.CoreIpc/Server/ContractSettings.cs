namespace UiPath.Ipc;

using System;

public sealed class ContractSettings
{
    public TaskScheduler? Scheduler { get; set; }
    public BeforeCallHandler? BeforeIncomingCall { get; set; }
    internal ServiceFactory Service { get; }

    internal Type ContractType => Service.Type;
    internal object? ServiceInstance => Service.MaybeGetInstance();
    internal IServiceProvider? ServiceProvider => Service.MaybeGetServiceProvider();

    public ContractSettings(Type contractType, object? serviceInstance = null) : this(
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

    public ContractSettings(Type contractType, IServiceProvider serviceProvider) : this(
        new ServiceFactory.Injected()
        {
            Type = contractType ?? throw new ArgumentNullException(nameof(contractType)),
            ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider))
        })
    { }

    private ContractSettings(ServiceFactory service) => Service = service;

    internal ContractSettings(ContractSettings other)
    {
        Scheduler = other.Scheduler;
        BeforeIncomingCall = other.BeforeIncomingCall;
        Service = other.Service;
    }
}
