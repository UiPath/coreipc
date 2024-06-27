﻿namespace UiPath.CoreIpc;

using ConnectionFactory = Func<Connection, CancellationToken, Task<Connection>>;
using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;

public abstract class ServiceClientBuilder<TDerived, TInterface> where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
{
    protected readonly IServiceProvider _serviceProvider;
    protected ISerializer _serializer = new IpcJsonSerializer();
    protected TimeSpan _requestTimeout = Timeout.InfiniteTimeSpan;
    protected ILogger _logger;
    protected ConnectionFactory _connectionFactory;
    protected BeforeCallHandler _beforeCall;
    protected object _callbackInstance;
    protected TaskScheduler _taskScheduler;

    protected ServiceClientBuilder(Type callbackContract, IServiceProvider serviceProvider)
    {
        CallbackContract = callbackContract;
        _serviceProvider = serviceProvider;
    }

    internal Type CallbackContract { get; }

    public TDerived DontReconnect() => ConnectionFactory((connection, _) => Task.FromResult(connection));

    public TDerived ConnectionFactory(ConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        return (TDerived)this;
    }

    public TDerived BeforeCall(BeforeCallHandler beforeCall)
    {
        _beforeCall = beforeCall;
        return (TDerived)this;
    }

    public TDerived Logger(ILogger logger)
    {
        _logger = logger;
        return (TDerived)this;
    }

    public TDerived Logger(IServiceProvider serviceProvider) => Logger(serviceProvider.GetRequiredService<ILogger<TInterface>>());

    public TDerived Serializer(ISerializer serializer)
    {
        _serializer = serializer;
        return (TDerived) this;
    }

    public TDerived RequestTimeout(TimeSpan timeout)
    {
        _requestTimeout = timeout;
        return (TDerived) this;
    }

    protected abstract TInterface BuildCore(EndpointSettings serviceEndpoint);

    public TInterface Build()
    {
        if (CallbackContract == null)
        {
            return BuildCore(null);
        }
        if (_logger == null)
        {
            Logger(_serviceProvider);
        }
        return BuildCore(new(CallbackContract, _callbackInstance) { Scheduler = _taskScheduler, ServiceProvider = _serviceProvider });
    }
}

public readonly struct CallInfo
{
    public CallInfo(bool newConnection, MethodInfo method, object[] arguments)
    {
        NewConnection = newConnection;
        Method = method;
        Arguments = arguments;
    }
    public bool NewConnection { get; }
    public MethodInfo Method { get; }
    public object[] Arguments { get; }
}