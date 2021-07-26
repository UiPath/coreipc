using System;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace UiPath.CoreIpc.Tests
{
    public abstract class TestBase : IDisposable
    {
        protected const int MaxReceivedMessageSizeInMegabytes = 1;
        protected static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
        protected readonly IServiceProvider _serviceProvider;
        protected readonly AsyncContext _guiThread = new AsyncContextThread().Context;

        public TestBase()
        {
            _guiThread.SynchronizationContext.Send(() => Thread.CurrentThread.Name = "GuiThread");
            _serviceProvider = IpcHelpers.ConfigureServices();
        }

        protected TaskScheduler GuiScheduler => _guiThread.Scheduler;

        public virtual void Dispose()
        {
            _guiThread.Dispose();
        }
        protected TSettings Configure<TSettings>(TSettings listenerSettings) where TSettings : ListenerSettings
        {
            listenerSettings.RequestTimeout = RequestTimeout.Subtract(TimeSpan.FromSeconds(1));
            listenerSettings.MaxReceivedMessageSizeInMegabytes = MaxReceivedMessageSizeInMegabytes;
            listenerSettings.ConcurrentAccepts = 10;
            return listenerSettings;
        }
        protected abstract ServiceHostBuilder Configure(ServiceHostBuilder serviceHostBuilder);
    }
}