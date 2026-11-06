namespace UiPath.Ipc;

public abstract class IpcBase
{
    /// <summary>
    /// The optional default timeout for all invocation requests. Leaving it or explicitly setting it to <c>null</c> has the same effect as setting it to <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }
    public IServiceProvider? ServiceProvider { get; set; }
    public TaskScheduler? Scheduler { get; set; }
    /// <summary>
    /// Opt-in W3C trace-context propagation (traceparent, tracestate, baggage) for this endpoint's calls and callbacks,
    /// per direction (<see cref="TelemetryPropagationPolicy.Incoming"/>, <see cref="TelemetryPropagationPolicy.Outgoing"/>).
    /// Defaults to <see cref="TelemetryPropagationPolicy.None"/>: nothing is sent or read.
    /// </summary>
    public TelemetryPropagationPolicy TelemetryPropagation { get; set; }
}
