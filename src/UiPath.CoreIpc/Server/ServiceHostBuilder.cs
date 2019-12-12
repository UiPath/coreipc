using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    public class ServiceHostBuilder
    {
        private readonly List<Listener> _listeners = new List<Listener>();
        public ServiceHostBuilder(IServiceProvider serviceProvider) => ServiceProvider = serviceProvider;
        internal IServiceProvider ServiceProvider { get; }
        internal IDictionary<string, EndpointSettings> Endpoints { get; } = new Dictionary<string, EndpointSettings>();
        public ServiceHostBuilder AddEndpoint(EndpointSettings settings)
        {
            settings.ServiceProvider = ServiceProvider;
            Endpoints.Add(settings.Name, settings);
            return this;
        }
        internal ServiceHostBuilder AddListener(Listener listener)
        {
            _listeners.Add(listener);
            return this;
        }
        public ServiceHost Build() => new ServiceHost(_listeners, Endpoints, ServiceProvider);
    }
    public static class ServiceHostBuilderExtensions
    {
        public static ServiceHostBuilder AddEndpoints(this ServiceHostBuilder serviceHostBuilder, IEnumerable<EndpointSettings> endpoints)
        {
            foreach (var endpoint in endpoints)
            {
                serviceHostBuilder.AddEndpoint(endpoints);
            }
            return serviceHostBuilder;
        }
        public static ServiceHostBuilder AddEndpoint<TContract>(this ServiceHostBuilder serviceHostBuilder, TContract serviceInstance = null) where TContract : class => 
            serviceHostBuilder.AddEndpoint(new EndpointSettings<TContract>(serviceInstance));
        public static ServiceHostBuilder AddEndpoint<TContract, TCallbackContract>(this ServiceHostBuilder serviceHostBuilder, TContract serviceInstance = null) where TContract : class where TCallbackContract : class =>
            serviceHostBuilder.AddEndpoint(new EndpointSettings<TContract, TCallbackContract>(serviceInstance));
    }
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddIpc(this IServiceCollection services)
        {
            services.AddSingleton<ISerializer, JsonSerializer>();
            return services;
        }
    }
    public class EndpointSettings
    {
        internal static string Default { get; } = "Default";
        public EndpointSettings(Type contract, object serviceInstance = null, Type callbackContract = null)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            Name = contract.Name;
            ServiceInstance = serviceInstance;
            CallbackContract = callbackContract;
            IOHelpers.Validate(contract);
        }
        internal string Name { get; private set; }
        internal TaskScheduler Scheduler { get; set; }
        internal object ServiceInstance { get; }
        internal Type Contract { get; }
        internal Type CallbackContract { get; }
        internal IServiceProvider ServiceProvider { get; set; }
        public bool IsDefault
        {
            get => Name == Default;
            set => Name = (value ? Default : Name);
        }
    }
    public class EndpointSettings<TContract> : EndpointSettings where TContract : class
    {
        public EndpointSettings(TContract serviceInstance = null, Type callbackContract = null) : base(typeof(TContract), serviceInstance, callbackContract) { }
    }
    public class EndpointSettings<TContract, TCallbackContract> : EndpointSettings<TContract> where TContract : class where TCallbackContract : class
    {
        public EndpointSettings(TContract serviceInstance = null) : base(serviceInstance, typeof(TCallbackContract)) { }
    }
}