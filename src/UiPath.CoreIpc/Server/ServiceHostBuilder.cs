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
        public ServiceHostBuilder AddEndpoint<TInterface>() where TInterface : class => AddEndpoint(new EndpointSettings<TInterface>());
        public ServiceHostBuilder AddEndpoint<TInterface, TCallback>() where TInterface : class where TCallback : class => AddEndpoint(new EndpointSettings<TInterface, TCallback>());
        internal ServiceHostBuilder AddListener(Listener listener)
        {
            _listeners.Add(listener);
            return this;
        }
        public ServiceHost Build() => new ServiceHost(_listeners, Endpoints, ServiceProvider);
    }
}