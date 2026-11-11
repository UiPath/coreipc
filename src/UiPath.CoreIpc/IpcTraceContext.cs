namespace UiPath.Ipc;

/// <summary>The W3C trace context (traceparent, tracestate, baggage) of an IPC call,
/// propagated only in the directions <see cref="IpcBase.TelemetryPropagation"/> allows.</summary>
public sealed class IpcTraceContext
{
    internal const string TraceParentHeader = "traceparent";
    internal const string TraceStateHeader = "tracestate";
    internal const string BaggageHeader = "baggage";

    private static readonly AsyncLocal<IpcTraceContext?> Override = new();

    // In its own class: Use reads Override, which runs this class's static initializer, and with propagation off nothing may
    // load DiagnosticSource.
    private static class Spans
    {
        public static readonly ActivitySource Source = new("UiPath.Ipc");
    }

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

        if (traceContext.TraceParent is { } traceParent && !TraceHeaders.IsValidTraceParent(traceParent))
        {
            throw new ArgumentException($"The {TraceParentHeader} must be a W3C traceparent.", nameof(traceContext));
        }

        if (traceContext.TraceParent is null && traceContext.Baggage is null)
        {
            throw new ArgumentException($"The trace context must have a {TraceParentHeader} or a {BaggageHeader}.", nameof(traceContext));
        }

        return Override.Push(traceContext);
    }

    /// <summary>A traceparent that breaks W3C Trace Context is ignored, and so is its tracestate (§3.3); tracestate members
    /// that break their grammar are dropped, and the tracestate and baggage are cut to the limits a sender keeps to.</summary>
    internal static IpcTraceContext? FromHeaders(Dictionary<string, string?>? headers)
    {
        if (headers is null)
        {
            return null;
        }

        var traceParent = GetHeader(headers, TraceParentHeader) is { } received && TraceHeaders.IsValidTraceParent(received) ? received : null;
        var traceState = traceParent is null ? null : GetHeader(headers, TraceStateHeader)?.LimitTraceState();
        var baggage = GetHeader(headers, BaggageHeader)?.LimitBaggage();
        return traceParent is null && baggage is null ? null : new(traceParent, traceState, baggage);
    }

    internal Activity StartActivity(string name)
    {
        ActivityContext? parent = TraceHeaders.TryParseTraceParent(TraceParent, TraceState, out var context) ? context : null;
        // W3C ids even where the default format is hierarchical (.NET Framework), so that a call with a baggage but no
        // traceparent still passes its baggage on from the new root activity.
        var activity = Spans.Source.HasListeners()
            ? Spans.Source.CreateActivity(name, ActivityKind.Server, parent ?? default, idFormat: ActivityIdFormat.W3C)
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
            var keys = new HashSet<string>(StringComparer.Ordinal);
            // AddBaggage puts each item first, so adding in reverse rebuilds the sender's order. Of a repeated key only the last
            // is added, so GetBaggageItem reads what JS, Python and OpenTelemetry read.
            for (var index = items.Count - 1; index >= 0; index--)
            {
                if (keys.Add(items[index].Key))
                {
                    activity.AddBaggage(items[index].Key, items[index].Value);
                }
            }
        }
        // Baggage goes in before Start so ActivityStarted listeners see it.
        return activity.Start();
    }

    /// <summary>Runs <paramref name="call"/> under the server span <paramref name="name"/>, marked as an error when it throws.
    /// The span is only started here so that a server with propagation off never loads DiagnosticSource, which a .NET
    /// Framework host may lack, or ship in another version without a binding redirect.</summary>
    internal async Task<T> RunInServerSpan<T>(string name, Func<Task<T>> call)
    {
        using var activity = StartActivity(name);
        try
        {
            return await call();
        }
        catch (Exception ex)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
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

    /// <summary>What one outgoing call sends: the <see cref="Use"/> scope, read once, and the client span it allows.</summary>
    internal sealed class OutgoingTrace : IDisposable
    {
        private readonly IpcTraceContext? _scope;
        private readonly Activity? _activity;

        private OutgoingTrace(IpcTraceContext? scope, Activity? activity)
        {
            _scope = scope;
            _activity = activity;
        }

        /// <summary>The innermost <see cref="Use"/> scope on the current async flow; only <see cref="Use"/> sets it.</summary>
        public static IpcTraceContext? Scope => Override.Value;

        /// <summary>Reads the scope and, unless it brings its own traceparent, starts the client span, a child of the ambient
        /// W3C <see cref="Activity.Current"/>: a plain <see cref="Activity"/> when nobody samples the source, so its ids still
        /// carry the caller's current flags.</summary>
        // Not inlined: inlined into ServiceClient.Invoke, it would make the JIT load DiagnosticSource with propagation off.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static OutgoingTrace Start(string name)
        {
            var scope = Scope;
            var activity = scope?.TraceParent is null && Activity.Current is { IdFormat: ActivityIdFormat.W3C }
                ? (Spans.Source.CreateActivity(name, ActivityKind.Client) ?? new Activity(name)).Start()
                : null;
            return new(scope, activity);
        }

        // The headers are written by hand, not through DistributedContextPropagator.Current, so they are the same whatever
        // DiagnosticSource the host loads and are what JS and Python read (Current is LegacyPropagator before DiagnosticSource 10
        // and W3CPropagator from 10, inbox on .NET 10; neither writes what JS and Python do).
        public Dictionary<string, string?>? Headers()
        {
            if (TraceContext() is not { } traceContext)
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

        public void SetError(string message) => _activity?.SetStatus(ActivityStatusCode.Error, message);

        public void Dispose()
        {
            if (_activity is null)
            {
                return;
            }
            if (Activity.Current == _activity)
            {
                _activity.Dispose();
                return;
            }
            // Stopping makes the span's parent current wherever it runs. A call made under SuppressFlow can end on a thread that
            // never had the span as current: a pool thread, or code that finishes the call inline under SuppressFlow too. An
            // async method gives that thread its context back when it returns, flow suppressed or not.
            StopInItsOwnContext(_activity).GetAwaiter().GetResult();
        }

#pragma warning disable CS1998 // Nothing to await: the method is async only for the context its builder restores.
        private static async Task StopInItsOwnContext(Activity activity) => activity.Dispose();
#pragma warning restore CS1998

        /// <summary>The client span's traceparent and tracestate, with the scope's baggage when there is a scope, else the span's
        /// own; the scope alone without a W3C span, as always when the scope has a traceparent.</summary>
        private IpcTraceContext? TraceContext()
        => _activity is { IdFormat: ActivityIdFormat.W3C }
            ? new(_activity.Id!, _activity.TraceStateString, _scope is null ? _activity.ToBaggageHeader() : _scope.Baggage)
            : _scope;
    }
}
