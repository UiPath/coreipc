using System.Net;
using UiPath.Ipc;
using UiPath.Ipc.Transport.Tcp;

namespace UiPath.Ipc.BackCompat;

public class TcpClientBuilder<TInterface> : TcpClientBuilderBase<TcpClientBuilder<TInterface>, TInterface> where TInterface : class
{
    public TcpClientBuilder(IPEndPoint endPoint) : base(endPoint) { }
}

public abstract class TcpClientBuilderBase<TDerived, TInterface> : ServiceClientBuilder<TDerived, TInterface> where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
{
    private readonly IPEndPoint _endPoint;

    protected TcpClientBuilderBase(IPEndPoint endPoint, Type callbackContract = null, IServiceProvider serviceProvider = null) : base(callbackContract, serviceProvider) =>
        _endPoint = endPoint;

    protected override TInterface BuildCore(EndpointSettings? serviceEndpoint)
    => new TcpClient()
        {
            EndPoint = _endPoint,
            Serializer = Serializer,
            RequestTimeout = RequestTimeout,
            Logger = Logger,
            ConnectionFactory = ConfiguredConnectionFactory,
            BeforeCall = BeforeCall,
            Callbacks = serviceEndpoint.ToEndpointCollection()
        }
        .GetProxyFactory()
        .GetProxy<TInterface>();
}

public class TcpClientBuilder<TInterface, TCallbackInterface> : TcpClientBuilderBase<TcpClientBuilder<TInterface, TCallbackInterface>, TInterface> where TInterface : class where TCallbackInterface : class
{
    public TcpClientBuilder(IPEndPoint endPoint, IServiceProvider serviceProvider) : base(endPoint, typeof(TCallbackInterface), serviceProvider) { }

    public TcpClientBuilder<TInterface, TCallbackInterface> CallbackInstance(TCallbackInterface singleton)
    {
        ConfiguredCallbackInstance = singleton;
        return this;
    }

    public TcpClientBuilder<TInterface, TCallbackInterface> TaskScheduler(TaskScheduler taskScheduler)
    {
        ConfiguredTaskScheduler = taskScheduler;
        return this;
    }
}
