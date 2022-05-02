using System;
using System.Diagnostics;
using UiPath.CoreIpc.Telemetry;

namespace UiPath.CoreIpc.Tests
{
    public class TestTelemetryProvider : ITelemetryProvider
    {
        public string CorrelationId => Trace.CorrelationManager.ActivityId.ToString();

        public string CurrentOperationId => CorrelationId;

        public ITelemetryOperation StartDependency(string name, string type, string target, string correlationId = null)
            => new TelemetryOperation(name);

        public ITelemetryOperation StartOperation(string name, string correlationId = null)
            => new TelemetryOperation(name);

        private class TelemetryOperation : ITelemetryOperation
        {
            private Guid _id = Guid.NewGuid();
            private Guid _parentId;
            private Stopwatch _timer;

            public TelemetryOperation(string _)
            {
                _timer = Stopwatch.StartNew();
                _parentId = Trace.CorrelationManager.ActivityId;
                Trace.CorrelationManager.ActivityId = _id;
            }

            public bool Success { get; set; }
            public string Status { get; set; }

            public void AddEvent(string name)
            {
                // noop
            }

            public void Dispose()
            {
                _timer?.Stop();
                Trace.CorrelationManager.ActivityId = _parentId;
            }
        }
    }
}
