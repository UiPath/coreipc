using System;

namespace UiPath.CoreIpc.Telemetry
{
    public interface ITelemetryOperation : IDisposable
    {
        void SetParentId(string correlationId);
        void Start();

        void End();
    }
}
