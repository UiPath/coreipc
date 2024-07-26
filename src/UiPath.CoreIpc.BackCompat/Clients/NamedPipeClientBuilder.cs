using UiPath.Ipc;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.BackCompat;

public class NamedPipeClientBuilder<TInterface, TCallbackInterface> : NamedPipeClientBuilderBase<NamedPipeClientBuilder<TInterface, TCallbackInterface>, TInterface> where TInterface : class where TCallbackInterface : class
{
    public NamedPipeClientBuilder(string pipeName, IServiceProvider serviceProvider) : base(pipeName, typeof(TCallbackInterface), serviceProvider) { }

    public NamedPipeClientBuilder<TInterface, TCallbackInterface> CallbackInstance(TCallbackInterface singleton)
    {
        ConfiguredCallbackInstance = singleton;
        return this;
    }

    public NamedPipeClientBuilder<TInterface, TCallbackInterface> TaskScheduler(TaskScheduler taskScheduler)
    {
        ConfiguredTaskScheduler = taskScheduler;
        return this;
    }
}

public class NamedPipeClientBuilder<TInterface> : NamedPipeClientBuilderBase<NamedPipeClientBuilder<TInterface>, TInterface> where TInterface : class
{
    public NamedPipeClientBuilder(string pipeName) : base(pipeName) { }
}

public abstract class NamedPipeClientBuilderBase<TDerived, TInterface> : ServiceClientBuilder<TDerived, TInterface> where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
{
    private readonly string _pipeName;
    private string _serverName = ".";
    private bool _allowImpersonation;

    protected NamedPipeClientBuilderBase(string pipeName, Type? callbackContract = null, IServiceProvider? serviceProvider = null) : base(callbackContract, serviceProvider) => _pipeName = pipeName;

    public TDerived ServerName(string serverName)
    {
        _serverName = serverName;
        return (this as TDerived)!;
    }

    /// <summary>
    /// Don't set this if you can connect to less privileged processes. 
    /// Allow impersonation is false by default to prevent an escalation of privilege attack.
    /// If a privileged process connects to a less privileged one and the proxy allows impersonation then the server could impersonate the client's identity.
    /// </summary>
    /// <returns>this</returns>
    public TDerived AllowImpersonation()
    {
        _allowImpersonation = true;
        return (this as TDerived)!;
    }

    protected override TInterface BuildCore(EndpointSettings? serviceEndpoint)
    => new NamedPipeClient()
    {
        ServerName = _serverName,
        PipeName = _pipeName,
        Serializer = Serializer,
        RequestTimeout = RequestTimeout,
        AllowImpersonation = _allowImpersonation,
        Logger = Logger,
        ConnectionFactory = ConfiguredConnectionFactory,
        BeforeCall = BeforeCall,
        Scheduler = ConfiguredTaskScheduler,
        ServiceProvider = _serviceProvider,
        Callbacks = serviceEndpoint.ToEndpointCollection()
    }
        .GetProxyFactory()
        .GetProxy<TInterface>();
}
