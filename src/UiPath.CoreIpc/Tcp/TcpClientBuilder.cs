using System.Net;

namespace UiPath.CoreIpc.Tcp;

public abstract class TcpClientBuilderBase<TDerived, TInterface> : ServiceClientBuilder<TDerived, TInterface> where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
{
    private readonly IPEndPoint _endPoint;

    protected TcpClientBuilderBase(IPEndPoint endPoint, Type callbackContract = null, IServiceProvider serviceProvider = null) : base(callbackContract, serviceProvider) =>
        _endPoint = endPoint;

    protected override TInterface BuildCore(EndpointSettings serviceEndpoint) =>
        new TcpClient<TInterface>(_endPoint, _serializer, _requestTimeout, _logger, _connectionFactory, _sslServer, _beforeCall, serviceEndpoint).CreateProxy();
}

public class TcpClientBuilder<TInterface> : TcpClientBuilderBase<TcpClientBuilder<TInterface>, TInterface> where TInterface : class
{
    public TcpClientBuilder(IPEndPoint endPoint) : base(endPoint){}
}

public class TcpClientBuilder<TInterface, TCallbackInterface> : TcpClientBuilderBase<TcpClientBuilder<TInterface, TCallbackInterface>, TInterface> where TInterface : class where TCallbackInterface : class
{
    public TcpClientBuilder(IPEndPoint endPoint, IServiceProvider serviceProvider) : base(endPoint, typeof(TCallbackInterface), serviceProvider) { }

    public TcpClientBuilder<TInterface, TCallbackInterface> CallbackInstance(TCallbackInterface singleton)
    {
        _callbackInstance = singleton;
        return this;
    }

    public TcpClientBuilder<TInterface, TCallbackInterface> TaskScheduler(TaskScheduler taskScheduler)
    {
        _taskScheduler = taskScheduler;
        return this;
    }
}