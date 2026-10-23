using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace UiPath.Ipc;

/// <summary>The directions in which an endpoint propagates the W3C trace context, see <see cref="IpcBase.TelemetryPropagation"/>.</summary>
[Flags]
public enum TelemetryPropagationPolicy
{
    None = 0,
    /// <summary>The calls and callbacks the endpoint receives run under an <see cref="Activity"/> parented by the received
    /// traceparent and expose <see cref="IpcContext.TraceContext"/>.</summary>
    Incoming = 1,
    /// <summary>The calls and callbacks the endpoint makes carry <see cref="IpcTelemetry.Context.Use"/>'s value, or else the ids
    /// of a per-call client <see cref="Activity"/> started as a child of a W3C <see cref="Activity.Current"/>.</summary>
    Outgoing = 2,
    Both = Incoming | Outgoing,
}

public static class IpcTelemetry
{
    /// <summary>The W3C trace context (traceparent, tracestate, baggage) of an IPC call,
    /// propagated only in the directions <see cref="IpcBase.TelemetryPropagation"/> allows.</summary>
    public sealed class Context
    {
        internal const string TraceParentHeader = "traceparent";
        internal const string TraceStateHeader = "tracestate";
        internal const string BaggageHeader = "baggage";

        private const int MaxTraceStateMembers = 32;
        private const int MaxBaggageMembers = 64;
        private const int MaxBaggageBytes = 8192;

        private static readonly AsyncLocal<Context?> Override = new();
        private static readonly ActivitySource Source = new("UiPath.Ipc");

        internal static Context? Current => Override.Value;

        public Context(string traceParent, string? traceState = null, string? baggage = null)
        {
            TraceParent = traceParent;
            TraceState = traceState.NullIfEmpty();
            Baggage = baggage.NullIfEmpty();
        }

        public string TraceParent { get; }
        public string? TraceState { get; }
        /// <summary>W3C Baggage format: comma-separated, percent-encoded key=value members.</summary>
        public string? Baggage { get; }

        public override string ToString() => TraceParent;

        /// <summary>Makes <paramref name="traceContext"/> the trace context of every outgoing call (forward calls and
        /// callbacks) on the current async flow until disposed, winning over <see cref="Activity.Current"/>.
        /// Scopes nest; disposing restores the previous value.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="traceContext"/> is null.</exception>
        /// <exception cref="ArgumentException">A value breaks its W3C rule: traceparent must parse with
        /// <see cref="ActivityContext.TryParse(string, string, out ActivityContext)"/>, tracestate at most 32 list members,
        /// baggage at most 64 list members and 8192 UTF-8 bytes.</exception>
        public static IDisposable Use(Context traceContext)
        {
            if (traceContext is null)
            {
                throw new ArgumentNullException(nameof(traceContext));
            }

            if (!traceContext.IsValid(out string? error))
            {
                throw new ArgumentException(error, nameof(traceContext));
            }

            return Push(traceContext);
        }

        internal static IDisposable Push(Context? traceContext)
        {
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

            var headers = new Dictionary<string, string?> { [TraceParentHeader] = traceContext.TraceParent };
            if (traceContext.TraceState is { } traceState)
            {
                headers[TraceStateHeader] = traceState;
            }
            if (traceContext.Baggage is { } baggage)
            {
                headers[BaggageHeader] = baggage;
            }
            return headers;
        }

        internal static Context? FromHeaders(Dictionary<string, string?>? headers)
        {
            if (headers is null || GetHeader(headers, TraceParentHeader) is not { } traceParent)
            {
                return null;
            }

            return new(traceParent, GetHeader(headers, TraceStateHeader), GetHeader(headers, BaggageHeader));
        }

        internal Activity StartActivity(string name)
        {
            var activity = Source.HasListeners() && ActivityContext.TryParse(TraceParent, TraceState, out var parent)
                ? Source.CreateActivity(name, ActivityKind.Server, new ActivityContext(parent.TraceId, parent.SpanId, parent.TraceFlags, parent.TraceState, isRemote: true))
                : null;

            if (activity is null)
            {
                // Without a sampled listener there is no source activity, yet Activity.Current must still
                // carry the remote trace so nested calls propagate it.
                activity = new Activity(name).SetParentId(TraceParent);
                activity.TraceStateString = TraceState;
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

        private static Context? FromActivity(Activity? activity)
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

        private bool IsValid([NotNullWhen(returnValue: false)] out string? error)
        {
            if (!ActivityContext.TryParse(TraceParent, TraceState, out _))
            {
                error = $"The {TraceParentHeader} must be a W3C traceparent.";
            }
            else if (TraceState is not null && TraceState.CountMembers() > MaxTraceStateMembers)
            {
                error = $"The {TraceStateHeader} must have at most {MaxTraceStateMembers} list members.";
            }
            else if (Baggage is not null && Baggage.CountMembers() > MaxBaggageMembers)
            {
                error = $"The {BaggageHeader} must have at most {MaxBaggageMembers} list members.";
            }
            else if (Baggage is not null && Encoding.UTF8.GetByteCount(Baggage) > MaxBaggageBytes)
            {
                error = $"The {BaggageHeader} must be at most {MaxBaggageBytes} bytes (UTF-8).";
            }
            else
            {
                error = null;
            }
            return error is null;
        }

        private sealed class Scope : IDisposable
        {
            private readonly Context? _previous;
            private bool _disposed;

            public Scope(Context? previous) => _previous = previous;

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

    /// <summary>The W3C baggage of <paramref name="activity"/> and its parents, in <see cref="Activity.Baggage"/> order, or <c>null</c> when there is none.</summary>
    internal static string? ToBaggageHeader(this Activity activity)
    => activity.Baggage.Any()
        ? string.Join(",", activity.Baggage.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? "")}"))
        : null;

    private static List<KeyValuePair<string, string>> DecodeBaggage(this string baggage)
    {
        var items = new List<KeyValuePair<string, string>>();
        foreach (var member in baggage.Split(','))
        {
            var separator = member.IndexOf('=');
            items.Add(new(Uri.UnescapeDataString(member.Substring(0, separator)), Uri.UnescapeDataString(member.Substring(separator + 1))));
        }
        return items;
    }

    private static int CountMembers(this string list) => list.Split(',').Length;

    private static string? NullIfEmpty(this string? value) => string.IsNullOrEmpty(value) ? null : value;
}
