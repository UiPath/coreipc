using System;
using System.Collections.Generic;

namespace UiPath.Ipc
{
    public class ServiceHostBuilder
    {
        private readonly List<ServiceEndpoint> _endpoints = new List<ServiceEndpoint>();

        public ServiceHostBuilder(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        internal IServiceProvider ServiceProvider { get; }

        public ServiceHostBuilder AddEndpoint(ServiceEndpoint endpoint)
        {
            _endpoints.Add(endpoint);
            return this;
        }

        public ServiceHost Build()
        {
            return new ServiceHost(_endpoints, ServiceProvider);
        }
    }
}
