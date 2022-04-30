using System;
using System.Diagnostics;
using UiPath.CoreIpc.Telemetry;

namespace UiPath.CoreIpc.Tests
{
    public class TestTelemetryProvider : ITelemetryProvider
    {
        public string CorrelationId => Trace.CorrelationManager.ActivityId.ToString();

        public ITelemetryOperation CreateOperation(string name)
        {
            return new TelemetryOperation(name);
        }

        private class TelemetryOperation : ITelemetryOperation
        {
            private Guid _id = Guid.NewGuid();
            private Guid _parentId;
            private Stopwatch _timer;
            private readonly string _name;

            public TelemetryOperation(string name)
            {
                _name = name;
            }

            public void Dispose()
            {
                _timer?.Stop();
                Trace.CorrelationManager.ActivityId = _parentId;
            }

            public void SetParentId(string correlationId)
            {
            }

            public void Start()
            {
                _timer = Stopwatch.StartNew();
                _parentId = Trace.CorrelationManager.ActivityId;
                Trace.CorrelationManager.ActivityId = _id;
            }

            public void End()
            {
                _timer?.Stop();
            }
        }
    }
}
