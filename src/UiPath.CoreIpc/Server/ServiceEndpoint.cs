using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Security;
using System.Diagnostics;

namespace UiPath.CoreIpc
{
    public class ServiceEndpoint
    {
        private TaskScheduler _scheduler;
        internal ServiceEndpoint(IServiceProvider serviceProvider, EndpointSettings settings, ILogger logger)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        public ILogger Logger { get; }
        internal EndpointSettings Settings { get; }
        public string Name => Settings.Name;
        public IServiceProvider ServiceProvider { get; }
        public TaskScheduler Scheduler { get => _scheduler; set => _scheduler = value ?? TaskScheduler.Default; }
    }
}