using System.Net;

namespace UiPath.Ipc.Tcp;

public abstract class TcpClientBuilderBase<TDerived, TInterface> : ServiceClientBuilder<TDerived, TInterface> where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
{
    private readonly IPEndPoint _endPoint;

    protected TcpClientBuilderBase(IPEndPoint endPoint)
    => _endPoint = endPoint;

    protected override TInterface BuildCore() =>
        new TcpClient<TInterface>(_endPoint, _serializer, _requestTimeout, _logger, _connectionFactory, _beforeCall).CreateProxy();
}

public class TcpClientBuilder<TInterface> : TcpClientBuilderBase<TcpClientBuilder<TInterface>, TInterface> where TInterface : class
{
    public TcpClientBuilder(IPEndPoint endPoint) : base(endPoint) { }
}
