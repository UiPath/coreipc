using System;
using System.Collections.Generic;

namespace UiPath.CoreIpc
{
    public class ServiceHostBuilder
    {
        private readonly Dictionary<string, EndpointSettings> _endpoints = new Dictionary<string, EndpointSettings>();
        private readonly List<Listener> _listeners = new List<Listener>();

        public ServiceHostBuilder(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        internal IServiceProvider ServiceProvider { get; }

        public ServiceHostBuilder AddEndpoint(EndpointSettings settings)
        {
            settings.ServiceProvider = ServiceProvider;
            _endpoints.Add(settings.Name, settings);
            return this;
        }

        internal ServiceHostBuilder AddListener(Listener listener)
        {
            _listeners.Add(listener);
            listener.Endpoints = _endpoints;
            return this;
        }

        public ServiceHost Build()
        {
            return new ServiceHost(_listeners, _endpoints, ServiceProvider);
        }
    }
}
