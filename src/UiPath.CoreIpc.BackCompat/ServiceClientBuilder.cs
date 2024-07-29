using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UiPath.Ipc;

namespace UiPath.Ipc.BackCompat;

public abstract class ServiceClientBuilder<TDerived, TInterface>
    : ServiceClientBuilder<TInterface>
    where TInterface : class
    where TDerived : ServiceClientBuilder<TDerived, TInterface>
{
    protected ServiceClientBuilder(Type callbackContract, IServiceProvider serviceProvider) : base(callbackContract, serviceProvider)
    {
    }
}

public abstract class ServiceClientBuilder<T> : ServiceClientBuilder where T : class
{
    protected ServiceClientBuilder(Type callbackContract, IServiceProvider serviceProvider) : base(callbackContract, serviceProvider) { }

    protected abstract T BuildCore(EndpointSettings? serviceEndpoint);

    public T Build()
    {
        if (CallbackContract == null)
        {
            return BuildCore(null);
        }
        if (Logger == null)
        {
            this.Logger(_serviceProvider);
        }
        return BuildCore(CreateEndpointSettings());

        EndpointSettings CreateEndpointSettings()
        {
            if (ConfiguredCallbackInstance is null)
            {
                return new(CallbackContract, _serviceProvider) { Scheduler = ConfiguredTaskScheduler };
            }

            return new(CallbackContract, ConfiguredCallbackInstance) { Scheduler = ConfiguredTaskScheduler };
        }
    }
}

public abstract class ServiceClientBuilder
{
    protected internal Type CallbackContract { get; }
    protected readonly IServiceProvider _serviceProvider;

    protected internal ILogger? Logger;
    protected internal object? ConfiguredCallbackInstance;
    protected internal TaskScheduler? ConfiguredTaskScheduler;
    protected internal TimeSpan RequestTimeout = Timeout.InfiniteTimeSpan;

    protected internal BeforeCallHandler? BeforeCall;
    protected internal ConnectionFactory? ConfiguredConnectionFactory;

    protected internal ISerializer? Serializer;

    protected ServiceClientBuilder(Type callbackContract, IServiceProvider serviceProvider)
    {
        CallbackContract = callbackContract;
        _serviceProvider = serviceProvider;
    }
}

public static class ServiceClientBuilderExtensions
{
    public static ServiceClientBuilder<T> TaskScheduler<T>(this ServiceClientBuilder<T> builder, TaskScheduler taskScheduler)
    where T : class
    {
        builder.ConfiguredTaskScheduler = taskScheduler;
        return builder;
    }

    public static TBuilder Logger<TBuilder>(this TBuilder builder, ILogger logger)
    where TBuilder : ServiceClientBuilder
    {
        builder.Logger = logger;
        return builder;
    }

    public static TBuilder Logger<TBuilder>(this TBuilder builder, IServiceProvider serviceProvider)
    where TBuilder : ServiceClientBuilder
    => builder.Logger(serviceProvider.GetRequiredService<ILogger<TBuilder>>());

    public static TBuilder RequestTimeout<TBuilder>(this TBuilder builder, TimeSpan requestTimeout)
    where TBuilder : ServiceClientBuilder
    {
        builder.RequestTimeout = requestTimeout;
        return builder;
    }

    public static TBuilder BeforeCall<TBuilder>(this TBuilder builder, BeforeCallHandler beforeCall)
    where TBuilder : ServiceClientBuilder
    {
        builder.BeforeCall = beforeCall;
        return builder;
    }

    public static TBuilder DontReconnect<TBuilder>(this TBuilder builder)
    where TBuilder : ServiceClientBuilder
    => builder.ConnectionFactory((network, _) => Task.FromResult(network));

    public static TBuilder ConnectionFactory<TBuilder>(this TBuilder builder, ConnectionFactory connectionFactory)
    where TBuilder : ServiceClientBuilder
    {
        builder.ConfiguredConnectionFactory = connectionFactory;
        return builder;
    }

}