using System;

namespace UiPath.CoreIpc.Telemetry
{
    public interface ITelemetryOperation : IDisposable
    {
        public bool Success { get; set; }

        public string Status { get; set; }

        public void AddEvent(string name);
    }
}
