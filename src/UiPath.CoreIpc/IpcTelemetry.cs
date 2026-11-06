namespace UiPath.Ipc;

/// <summary>The directions in which an endpoint propagates the W3C trace context, see <see cref="IpcBase.TelemetryPropagation"/>.</summary>
[Flags]
public enum TelemetryPropagationPolicy
{
    None = 0,
    /// <summary>The calls and callbacks the endpoint receives run under an <see cref="Activity"/> parented by the received
    /// traceparent and expose <see cref="IpcContext.TraceContext"/>.</summary>
    Incoming = 1,
    /// <summary>The calls and callbacks the endpoint makes carry <see cref="IpcTraceContext.Use"/>'s value, or else the ids
    /// of a per-call client <see cref="Activity"/> started as a child of a W3C <see cref="Activity.Current"/>.</summary>
    Outgoing = 2,
    Both = Incoming | Outgoing,
}

/// <summary>The W3C trace context (traceparent, tracestate, baggage) of an IPC call,
/// propagated only in the directions <see cref="IpcBase.TelemetryPropagation"/> allows.</summary>
public sealed class IpcTraceContext
{
    internal const string TraceParentHeader = "traceparent";
    internal const string TraceStateHeader = "tracestate";
    internal const string BaggageHeader = "baggage";

    private static readonly AsyncLocal<IpcTraceContext?> Override = new();
    private static readonly ActivitySource Source = new("UiPath.Ipc");

    internal static IpcTraceContext? Current => Override.Value;

    /// <param name="traceParent">Optional: W3C Baggage doesn't depend on Trace Context, so a baggage can travel alone.
    /// The tracestate travels only with a traceparent.</param>
    public IpcTraceContext(string? traceParent, string? traceState = null, string? baggage = null)
    {
        TraceParent = traceParent.NullIfEmpty();
        TraceState = traceState.NullIfEmpty();
        Baggage = baggage.NullIfEmpty();
    }

    public string? TraceParent { get; }
    public string? TraceState { get; }
    /// <summary>W3C Baggage format: comma-separated, percent-encoded key=value members.</summary>
    public string? Baggage { get; }

    public override string ToString() => TraceParent ?? "";

    /// <summary>Makes <paramref name="traceContext"/> the trace context of every outgoing call (forward calls and
    /// callbacks) on the current async flow until disposed, winning over <see cref="Activity.Current"/>.
    /// Scopes nest; disposing restores the previous value.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="traceContext"/> is null.</exception>
    /// <exception cref="ArgumentException">The traceparent breaks W3C Trace Context §3.2.</exception>
    public static IDisposable Use(IpcTraceContext traceContext)
    {
        if (traceContext is null)
        {
            throw new ArgumentNullException(nameof(traceContext));
        }

        if (traceContext.TraceParent is { } traceParent && !TraceHeaders.IsValidTraceParent(traceParent))
        {
            throw new ArgumentException($"The {TraceParentHeader} must be a W3C traceparent.", nameof(traceContext));
        }

        var previous = Override.Value;
        Override.Value = traceContext;
        return new Scope(previous);
    }

    /// <summary>The client span of an outgoing call, a child of the ambient W3C <see cref="Activity.Current"/>;
    /// a plain <see cref="Activity"/> when nobody samples the source, so its ids still carry the caller's current flags.</summary>
    internal static Activity? StartClientActivity(string name)
    => Current is null && Activity.Current is { IdFormat: ActivityIdFormat.W3C }
        ? (Source.CreateActivity(name, ActivityKind.Client) ?? new Activity(name)).Start()
        : null;

    internal static Dictionary<string, string?>? GetOutgoingHeaders(Activity? clientActivity)
    {
        if ((Current ?? FromActivity(clientActivity)) is not { } traceContext)
        {
            return null;
        }

        var headers = new Dictionary<string, string?>();
        if (traceContext.TraceParent is { } traceParent && TraceHeaders.IsValidTraceParent(traceParent))
        {
            headers[TraceParentHeader] = traceParent;
            if (traceContext.TraceState?.LimitTraceState() is { } traceState)
            {
                headers[TraceStateHeader] = traceState;
            }
        }
        if (traceContext.Baggage?.LimitBaggage() is { } baggage)
        {
            headers[BaggageHeader] = baggage;
        }
        return headers.Count is 0 ? null : headers;
    }

    /// <summary>A traceparent that breaks W3C Trace Context is ignored, and so is its tracestate (§3.3);
    /// tracestate members that break their grammar are dropped.</summary>
    internal static IpcTraceContext? FromHeaders(Dictionary<string, string?>? headers)
    {
        if (headers is null)
        {
            return null;
        }

        var traceParent = GetHeader(headers, TraceParentHeader) is { } received && TraceHeaders.IsValidTraceParent(received) ? received : null;
        var traceState = traceParent is null ? null : GetHeader(headers, TraceStateHeader)?.ValidTraceState();
        var baggage = GetHeader(headers, BaggageHeader).NullIfEmpty();
        return traceParent is null && baggage is null ? null : new(traceParent, traceState, baggage);
    }

    internal Activity StartActivity(string name)
    {
        ActivityContext? parent = TraceParent is { } traceParent ? TraceHeaders.ParseTraceParent(traceParent, TraceState) : null;
        var activity = Source.HasListeners()
            ? Source.CreateActivity(name, ActivityKind.Server, parent ?? default)
            : null;

        if (activity is null)
        {
            // Without a sampled listener there is no source activity, yet Activity.Current must still
            // carry the remote trace so nested calls propagate it.
            activity = new Activity(name);
            if (parent is { } remote)
            {
                activity.SetParentId(remote.TraceId, remote.SpanId, remote.TraceFlags);
                activity.TraceStateString = remote.TraceState;
            }
        }

        if (Baggage is not null)
        {
            var items = Baggage.DecodeBaggage();
            // AddBaggage puts each item first, so adding in reverse rebuilds the sender's Activity.Baggage order.
            for (var index = items.Count - 1; index >= 0; index--)
            {
                activity.AddBaggage(items[index].Key, items[index].Value);
            }
        }
        // Baggage goes in before Start so ActivityStarted listeners see it.
        return activity.Start();
    }

    private static IpcTraceContext? FromActivity(Activity? activity)
    {
        if (activity is not { IdFormat: ActivityIdFormat.W3C })
        {
            return null;
        }

        return new(activity.Id!, activity.TraceStateString, activity.ToBaggageHeader());
    }

    private static string? GetHeader(Dictionary<string, string?> headers, string name)
    {
        if (headers.TryGetValue(name, out var value))
        {
            return value;
        }
        foreach (var pair in headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }
        return null;
    }

    private sealed class Scope : IDisposable
    {
        private readonly IpcTraceContext? _previous;
        private bool _disposed;

        public Scope(IpcTraceContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Override.Value = _previous;
        }
    }
}

internal static class IpcTelemetry
{
    /// <summary>The W3C baggage of <paramref name="activity"/> and its parents, in <see cref="Activity.Baggage"/> order, or <c>null</c> when there is none.</summary>
    internal static string? ToBaggageHeader(this Activity activity)
    => activity.Baggage.Any()
        ? string.Join(",", activity.Baggage.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? "")}"))
        : null;

    internal static string? NullIfEmpty(this string? value) => string.IsNullOrEmpty(value) ? null : value;
}
