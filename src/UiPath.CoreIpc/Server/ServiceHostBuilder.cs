using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

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
}