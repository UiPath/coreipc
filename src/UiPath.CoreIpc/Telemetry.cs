namespace UiPath.Ipc;

/// <summary>Writes and reads the traceparent, tracestate and baggage headers through DiagnosticSource's W3C propagator.</summary>
internal static class Telemetry
{
    private static readonly ActivitySource Source = new("UiPath.Ipc");
    // Not DistributedContextPropagator.Current, which a host can replace.
    private static readonly DistributedContextPropagator W3C = DistributedContextPropagator.CreateW3CPropagator();

    // Answers only the three exact names, so a Correlation-Context header is never read as baggage.
    private static void GetHeader(object? carrier, string name, out string? value, out IEnumerable<string>? values)
    {
        value = null;
        values = null;
        if (name is "traceparent" or "tracestate" or "baggage")
        {
            ((Dictionary<string, string>)carrier!).TryGetValue(name, out value);
        }
    }

    /// <summary>Runs <paramref name="call"/> under a server span parented by the received headers, marked as an error when it
    /// throws. Without a valid traceparent or any baggage, it runs with no span.</summary>
    internal static async Task<object?> RunInServerSpan(Dictionary<string, string> headers, string name, Func<Task<object?>> call)
    {
        using var activity = StartServerSpan(headers, name);
        try
        {
            return await call();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private static Activity? StartServerSpan(Dictionary<string, string> headers, string name)
    {
        W3C.ExtractTraceIdAndState(headers, GetHeader, out var traceParent, out var traceState);
        ActivityContext parent = default;
        // The propagator lets through a higher version longer than 55 characters; its first 55 are the fields we know.
        var hasParent = traceParent is not null
            && ActivityContext.TryParse(traceParent.Length > 55 ? traceParent.Substring(0, 55) : traceParent, traceState, isRemote: true, out parent);
        var baggage = W3C.ExtractBaggage(headers, GetHeader);
        if (!hasParent && baggage is null)
        {
            return null;
        }

        // W3C ids even where the default format is hierarchical (.NET Framework), so that nested calls send them.
        var activity = Source.CreateActivity(name, ActivityKind.Server, parent, idFormat: ActivityIdFormat.W3C);
        if (activity is null)
        {
            // Nobody samples the source, yet Activity.Current must still carry the remote trace so nested calls propagate it.
            activity = new Activity(name).SetIdFormat(ActivityIdFormat.W3C);
            if (hasParent)
            {
                activity.SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags);
                activity.TraceStateString = parent.TraceState;
            }
        }

        // The propagator returns the members last first, and AddBaggage puts each one first, so this rebuilds the header's
        // order, and GetBaggageItem reads the first of a repeated key.
        foreach (var item in baggage ?? [])
        {
            activity.AddBaggage(item.Key, item.Value);
        }
        // Baggage goes in before Start so ActivityStarted listeners see it.
        return activity.Start();
    }

    /// <summary>The client span of one outgoing call, and the headers it sends.</summary>
    internal sealed class OutgoingTrace : IDisposable
    {
        private readonly Activity _activity;

        private OutgoingTrace(Activity activity) => _activity = activity;

        /// <summary>Starts the client span, a child of a W3C <see cref="Activity.Current"/>: a plain <see cref="Activity"/> when
        /// nobody samples the source, so the server still sees a span of its own as the parent. Null when there's no W3C
        /// <see cref="Activity.Current"/>.</summary>
        public static OutgoingTrace? Start(string name)
        => Activity.Current is { IdFormat: ActivityIdFormat.W3C }
            // W3C even where the host forces hierarchical ids, or Inject would write no traceparent.
            ? new((Source.CreateActivity(name, ActivityKind.Client, default(ActivityContext), idFormat: ActivityIdFormat.W3C)
                ?? new Activity(name).SetIdFormat(ActivityIdFormat.W3C)).Start())
            : null;

        public Dictionary<string, string> Headers()
        {
            var headers = new Dictionary<string, string>();
            W3C.Inject(_activity, headers, static (carrier, name, value) => ((Dictionary<string, string>)carrier!)[name] = value);
            return headers;
        }

        public void SetError(string message) => _activity.SetStatus(ActivityStatusCode.Error, message);

        public void Dispose()
        {
            if (Activity.Current == _activity)
            {
                _activity.Dispose();
                return;
            }
            // Stopping makes the span's parent current, and a call made under SuppressFlow can end on a thread that never had
            // the span as current.
            StopLeavingCurrentAsIs(_activity).GetAwaiter().GetResult();
        }

#pragma warning disable CS1998 // Nothing to await: the method is async only for the context its builder restores.
        // Stops the span in a copy of this thread's context, which the async builder throws away on return.
        private static async Task StopLeavingCurrentAsIs(Activity activity) => activity.Dispose();
#pragma warning restore CS1998
    }
}
