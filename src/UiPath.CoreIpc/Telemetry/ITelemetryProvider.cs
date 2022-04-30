namespace UiPath.CoreIpc.Telemetry
{
    public interface ITelemetryProvider
    {
        ITelemetryOperation CreateOperation(string name);

        string CorrelationId { get; }
    }
}
