namespace UiPath.Ipc;

/// <summary>The W3C trace context (traceparent, tracestate, baggage) of an IPC call,
/// propagated only in the directions <see cref="IpcBase.TelemetryPropagation"/> allows.</summary>
public sealed class IpcTraceContext
{
    internal const string TraceParentHeader = "traceparent";
    internal const string TraceStateHeader = "tracestate";
    internal const string BaggageHeader = "baggage";

    internal static readonly AsyncLocal<IpcTraceContext?> Override = new();
    private static readonly ActivitySource Source = new("UiPath.Ipc");

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
    /// callbacks) on the current async flow until disposed. With a traceparent it wins over <see cref="Activity.Current"/>
    /// outright: only its own traceparent, tracestate and baggage are sent. Without one, the calls keep the ambient trace
    /// (the traceparent and tracestate of a client span under a W3C <see cref="Activity.Current"/>) and send this baggage
    /// with it, or alone when there is no such activity. To send nothing, make the call with no scope and
    /// <see cref="Activity.Current"/> set to null. Scopes nest, and an inner one replaces the outer one; disposing restores the previous value.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="traceContext"/> is null.</exception>
    /// <exception cref="ArgumentException">The traceparent breaks W3C Trace Context §3.2, or there is neither a traceparent
    /// nor a baggage.</exception>
    public static IDisposable Use(IpcTraceContext traceContext)
    {
        if (traceContext is null)
        {
            throw new ArgumentNullException(nameof(traceContext));
        }

        if (traceContext.TraceParent is { } traceParent && !TraceHeaders.TryParseTraceParent(traceParent, null, out _))
        {
            throw new ArgumentException($"The {TraceParentHeader} must be a W3C traceparent.", nameof(traceContext));
        }

        if (traceContext.TraceParent is null && traceContext.Baggage is null)
        {
            throw new ArgumentException($"The trace context must have a {TraceParentHeader} or a {BaggageHeader}.", nameof(traceContext));
        }

        return Override.Push(traceContext);
    }

    /// <summary>The client span of an outgoing call, a child of the ambient W3C <see cref="Activity.Current"/>, unless
    /// <paramref name="explicitContext"/> brings its own traceparent; a plain <see cref="Activity"/> when nobody samples
    /// the source, so its ids still carry the caller's current flags.</summary>
    internal static Activity? StartClientActivity(string name, IpcTraceContext? explicitContext)
    => explicitContext?.TraceParent is null && Activity.Current is { IdFormat: ActivityIdFormat.W3C }
        ? (Source.CreateActivity(name, ActivityKind.Client) ?? new Activity(name)).Start()
        : null;

    // The headers are written by hand, not through DistributedContextPropagator.Current, so they are the same on every runtime
    // and are what JS and Python read (Current is LegacyPropagator on .NET 6, 8 and net461, W3CPropagator on .NET 10).
    internal static Dictionary<string, string?>? GetOutgoingHeaders(IpcTraceContext? explicitContext, Activity? clientActivity)
    {
        if ((explicitContext is { TraceParent: not null } ? explicitContext : FromActivity(clientActivity, explicitContext)) is not { } traceContext)
        {
            return null;
        }

        var headers = new Dictionary<string, string?>();
        // Use checked its own traceparent, but an Activity can have an all-zero trace id (SetParentId(default, ...)).
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

        var traceParent = GetHeader(headers, TraceParentHeader) is { } received && TraceHeaders.TryParseTraceParent(received, null, out _) ? received : null;
        var traceState = traceParent is null ? null : GetHeader(headers, TraceStateHeader)?.ValidTraceState();
        var baggage = GetHeader(headers, BaggageHeader).NullIfEmpty();
        return traceParent is null && baggage is null ? null : new(traceParent, traceState, baggage);
    }

    internal Activity StartActivity(string name)
    {
        ActivityContext? parent = TraceHeaders.TryParseTraceParent(TraceParent, TraceState, out var context) ? context : null;
        // W3C ids even where the default format is hierarchical (.NET Framework), so that a call with a baggage but no
        // traceparent still passes its baggage on from the new root activity.
        var activity = Source.HasListeners()
            ? Source.CreateActivity(name, ActivityKind.Server, parent ?? default, idFormat: ActivityIdFormat.W3C)
            : null;

        if (activity is null)
        {
            // Without a sampled listener there is no source activity, yet Activity.Current must still
            // carry the remote trace so nested calls propagate it.
            activity = new Activity(name).SetIdFormat(ActivityIdFormat.W3C);
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

    /// <summary>The traceparent and tracestate of a W3C <paramref name="activity"/>, with <paramref name="explicitContext"/>'s
    /// baggage when there is an explicit context, else the activity's own; <paramref name="explicitContext"/> without such an activity.</summary>
    private static IpcTraceContext? FromActivity(Activity? activity, IpcTraceContext? explicitContext)
    {
        if (activity is not { IdFormat: ActivityIdFormat.W3C })
        {
            return explicitContext;
        }

        return new(activity.Id!, activity.TraceStateString, explicitContext is null ? activity.ToBaggageHeader() : explicitContext.Baggage);
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
}
