using System;
using System.Diagnostics;

namespace UiPath.CoreIpc.Telemetry
{
    internal class PocTelemetryProvider : ITelemetryProvider
    {
        public string CorrelationId => Trace.CorrelationManager.ActivityId.ToString();

        public ITelemetryOperation CreateOperation(string name) => new PocOperation(name);
    }

    internal class PocOperation : ITelemetryOperation
    {
        private readonly string _name;
        private Stopwatch _timer;
        private Guid _previousId;
        private Guid _id = Guid.NewGuid();
        private string _correlationId;

        public PocOperation(string name)
        {
            _name = name;
        }

        public void Dispose()
        {
            if (_timer.IsRunning)
            {
                End();
            }
        }

        public void SetParentId(string correlationId)
        {
            _correlationId = correlationId;
        }

        public void Start()
        {
            _previousId = Trace.CorrelationManager.ActivityId;
            Trace.CorrelationManager.ActivityId = _id;
            _timer = Stopwatch.StartNew();
        }

        public void End()
        {
            _timer.Stop();

            Trace.CorrelationManager.ActivityId = _previousId;

            using (var s = System.IO.File.AppendText(@"C:\temp\coreipc.txt"))
            {
                s.WriteLine($"{DateTime.Now}: {Process.GetCurrentProcess().Id}: {_name}: {_timer.ElapsedMilliseconds} [{_id}:{_correlationId}]");
            }
        }
    }
}
