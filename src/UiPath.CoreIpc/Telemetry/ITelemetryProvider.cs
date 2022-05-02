namespace UiPath.CoreIpc.Telemetry
{
    public interface ITelemetryProvider
    {

        ITelemetryOperation StartOperation(string name, string correlationId = null);

        ITelemetryOperation StartDependency(string name, string type, string target, string correlationId = null);

        string CorrelationId { get; }

        string CurrentOperationId { get; }
    }

    public static class Fix
    {
        public static ITelemetryProvider Crap = null;
    }
}
