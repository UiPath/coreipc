using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.Ipc
{
    public class ServiceHost : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly IReadOnlyCollection<ServiceEndpoint> _endpoints;
        private readonly ILogger<ServiceHost> _logger;

        public ServiceHost(IEnumerable<ServiceEndpoint> endpoints, IServiceProvider serviceProvider)
        {
            _endpoints = endpoints.ToArray();
            _logger = serviceProvider.GetRequiredService<ILogger<ServiceHost>>();
        }

        public IServiceProvider ServiceProvider => _endpoints.FirstOrDefault()?.ServiceProvider;

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
            var tasks = _endpoints.Select(endpoint => Task.Run(() =>
            {
                endpoint.Scheduler = taskScheduler;

                _logger.LogDebug($"Starting endpoint '{endpoint.Name}'...");

                _cancellationTokenSource.Token.Register(() => _logger.LogDebug($"Stopping endpoint '{endpoint.Name}'..."));

                return endpoint.ListenAsync(_cancellationTokenSource.Token).ContinueWith(_ => _logger.LogDebug($"Endpoint '{endpoint.Name}' stopped."));
            }));
            return Task.WhenAll(tasks);
        }
    }
}