namespace UiPath.CoreIpc.WebSockets;
public abstract class WebSocketClientBuilderBase<TDerived, TInterface> : ServiceClientBuilder<TDerived, TInterface> where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
{
    private readonly Uri _uri;
    protected WebSocketClientBuilderBase(Uri uri, Type callbackContract = null, IServiceProvider serviceProvider = null) : base(callbackContract, serviceProvider) =>
        _uri = uri;
    protected override TInterface BuildCore(EndpointSettings serviceEndpoint) =>
        new WebSocketClient<TInterface>(_uri, _requestTimeout, _logger, _connectionFactory, _sslServer, _beforeCall, serviceEndpoint).CreateProxy();
}
public class WebSocketClientBuilder<TInterface> : WebSocketClientBuilderBase<WebSocketClientBuilder<TInterface>, TInterface> where TInterface : class
{
    public WebSocketClientBuilder(Uri uri) : base(uri){}
}
public class WebSocketClientBuilder<TInterface, TCallbackInterface> : WebSocketClientBuilderBase<WebSocketClientBuilder<TInterface, TCallbackInterface>, TInterface> where TInterface : class where TCallbackInterface : class
{
    public WebSocketClientBuilder(Uri uri, IServiceProvider serviceProvider) : base(uri, typeof(TCallbackInterface), serviceProvider) { }
    public WebSocketClientBuilder<TInterface, TCallbackInterface> CallbackInstance(TCallbackInterface singleton)
    {
        _callbackInstance = singleton;
        return this;
    }
    public WebSocketClientBuilder<TInterface, TCallbackInterface> TaskScheduler(TaskScheduler taskScheduler)
    {
        _taskScheduler = taskScheduler;
        return this;
    }
}