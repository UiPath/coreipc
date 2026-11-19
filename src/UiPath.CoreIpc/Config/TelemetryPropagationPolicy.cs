namespace UiPath.Ipc;

/// <summary>The directions in which an endpoint propagates the W3C trace context, see <see cref="IpcBase.TelemetryPropagation"/>.</summary>
[Flags]
public enum TelemetryPropagationPolicy
{
    None = 0,
    /// <summary>The calls and callbacks the endpoint receives run under an <see cref="Activity"/> parented by the received
    /// traceparent and carrying the received baggage.</summary>
    Incoming = 1,
    /// <summary>The calls and callbacks the endpoint makes carry the ids of a per-call client <see cref="Activity"/> started as a
    /// child of a W3C <see cref="Activity.Current"/>, and its tracestate and baggage.</summary>
    Outgoing = 2,
    Both = Incoming | Outgoing,
}
