using System;
using System.Collections.Generic;

namespace UiPath.CoreIpc
{
    public class ServiceHostBuilder
    {
        private readonly Dictionary<string, ServiceEndpoint> _endpoints = new Dictionary<string, ServiceEndpoint>();
        private readonly List<Listener> _listeners = new List<Listener>();

        public ServiceHostBuilder(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        internal IServiceProvider ServiceProvider { get; }

        internal ServiceHostBuilder AddEndpoint(ServiceEndpoint endpoint)
        {
            _endpoints.Add(endpoint.Name, endpoint);
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
