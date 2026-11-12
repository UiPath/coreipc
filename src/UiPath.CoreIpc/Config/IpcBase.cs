namespace UiPath.Ipc;

public abstract class IpcBase
{
    /// <summary>
    /// The optional default timeout for all invocation requests. Leaving it or explicitly setting it to <c>null</c> has the same effect as setting it to <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }
    public IServiceProvider? ServiceProvider { get; set; }
    public TaskScheduler? Scheduler { get; set; }
    private IReadOnlyCollection<string>? _propagatedExceptionDataKeys = ["UiPath.ErrorInfo.Error"];
    /// <summary>
    /// The <see cref="Exception.Data"/> keys whose values are sent along with the errors of the calls this server or client handles,
    /// at every level of the inner exception chain, landing in the caller's <see cref="RemoteException"/>.<see cref="Exception.Data"/>.
    /// Each value must be a string, opaque to CoreIpc; put a structured payload inside it (e.g. as JSON).
    /// It starts as <c>["UiPath.ErrorInfo.Error"]</c>; assigning replaces it, and <c>null</c> or empty sends nothing of its own.
    /// </summary>
    /// <remarks>
    /// The entries a <see cref="RemoteException"/> received travel on with it, listed or not, when it fails a call this server or client handles,
    /// whether rethrown as is or as an inner exception. A key listed here takes its current string value instead, or is dropped without one.
    /// A <see cref="RemoteException"/> keeps at most the first 64 entries it receives at each level.
    /// </remarks>
    public IReadOnlyCollection<string>? PropagatedExceptionDataKeys
    {
        get => _propagatedExceptionDataKeys;
        // Copied here, so a collection that can't be read fails where it's assigned, not on a connection.
        set => _propagatedExceptionDataKeys = value?.ToArray();
    }
    /// <summary>
    /// Opt-in W3C trace-context propagation (traceparent, tracestate, baggage) for this endpoint's calls and callbacks,
    /// per direction (<see cref="TelemetryPropagationPolicy.Incoming"/>, <see cref="TelemetryPropagationPolicy.Outgoing"/>).
    /// Defaults to <see cref="TelemetryPropagationPolicy.None"/>: nothing is sent or read.
    /// </summary>
    public TelemetryPropagationPolicy TelemetryPropagation { get; set; }
}
