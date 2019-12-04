using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    public class ServiceHost : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly IDictionary<string, EndpointSettings> _endpoints;
        private readonly IReadOnlyCollection<Listener> _listeners;
        private readonly ILogger<ServiceHost> _logger;

        internal ServiceHost(IEnumerable<Listener> listeners, IDictionary<string, EndpointSettings> endpoints, IServiceProvider serviceProvider)
        {
            _endpoints = endpoints;
            _listeners = listeners.ToArray();
            _logger = serviceProvider.GetRequiredService<ILogger<ServiceHost>>();
        }

        public IServiceProvider ServiceProvider => _endpoints.Values.FirstOrDefault()?.ServiceProvider;

        public void Dispose()
        {
            if(_cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }

        public void Run()
        {
            RunAsync().Wait();
        }

        public Task RunAsync(TaskScheduler taskScheduler = null)
        {
            foreach (var endpoint in _endpoints.Values)
            {
                endpoint.Scheduler = taskScheduler;
            }
            var tasks = _listeners.Select(listener => Task.Run(() =>
            {
                _logger.LogDebug($"Starting endpoint '{listener}'...");
                _cancellationTokenSource.Token.Register(() => _logger.LogDebug($"Stopping endpoint '{listener}'..."));
                return listener.ListenAsync(_cancellationTokenSource.Token).ContinueWith(_ => _logger.LogDebug($"Endpoint '{listener}' stopped."));
            }));
            return Task.WhenAll(tasks);
        }
    }
}