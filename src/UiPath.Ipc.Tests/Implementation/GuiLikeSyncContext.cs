using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace UiPath.Ipc.Tests
{
    public class GuiLikeSyncContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object State)> _workQueue = new BlockingCollection<(SendOrPostCallback, object)>();

        private static readonly GuiLikeSyncContext Instance = new GuiLikeSyncContext();

        public static IDisposable Install() => new RevertSyncContext();

        sealed class RevertSyncContext : IDisposable
        {
            public SynchronizationContext SavedContext { get; }

            public RevertSyncContext()
            {
                SavedContext = Current;
                SetSynchronizationContext(Instance);
            }

            public void Dispose()
            {
                SetSynchronizationContext(SavedContext);
            }
        }

        private GuiLikeSyncContext()
        {
            new Thread(_ =>
            {
                Install();
                while(true)
                {
                    ProcessItem();
                }
            })
            { Name = "GuiThread", IsBackground = true }
            .Start();
        }

        private void ProcessItem()
        {
            var pair = _workQueue.Take();
            var callback = pair.Callback;
            lock(callback)
            {
                try
                {
                    callback(pair.State);
                }
                catch(Exception ex)
                {
                    Trace.WriteLine(ex.ToString());
                }
                Monitor.Pulse(callback);
            }
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            Trace.WriteLine("MySynchronizationContext.Post");
            _workQueue.Add((d, state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            Trace.WriteLine("MySynchronizationContext.Send");
            lock(d)
            {
                _workQueue.Add((d, state));
                Monitor.Wait(d);
            }
        }
    }
}