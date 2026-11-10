import { assertArgument } from '../../bcl';
import { TraceContextRuntime } from './TraceContextRuntime';
import { TraceHeaders } from './TraceHeaders';

/** The directions in which an `ipc` instance propagates the W3C trace context, set per address with `ipc.config.forAddress(...).setTelemetryPropagation` (or `forAnyAddress()`). */
export enum TelemetryPropagationPolicy {
    None = 0,
    /** The callbacks it receives run within the trace context they arrived with (`currentIncomingTraceContext`). */
    Incoming = 1,
    /** The calls it makes carry the ambient trace context (the active OpenTelemetry span on Node), with the values a `withTraceContext` scope sets taking precedence. */
    Outgoing = 2,
    Both = 3,
}

/**
 * The W3C trace context of an IPC call, propagated only in the directions `setTelemetryPropagation` allows for its address.
 */
export interface TraceContext {
    /** Optional: W3C Baggage doesn't depend on Trace Context, so a baggage can travel alone. The tracestate travels only with a traceparent. */
    traceparent?: string;
    tracestate?: string;
    /** W3C Baggage format: comma-separated, percent-encoded key=value members. */
    baggage?: string;
}

/**
 * Runs `callback` with `traceContext` as the trace context of every outgoing call made within it; callback handlers
 * never see it, since each callback runs isolated from whatever opened the connection.
 * With a `traceparent` it wins outright over the ambient OpenTelemetry context (read on Node only; the browser
 * package never reads it): only its own values are sent. Without one, its `baggage` is sent with the ambient
 * traceparent and tracestate, if any.
 * Scopes nest, and an inner one replaces the outer one; the previous one is restored when `callback` returns.
 *
 * On Node the scope follows the awaits inside `callback`. In the browser it is synchronous: it only applies
 * to calls started before `callback` first awaits (a proxy call builds its request synchronously, so
 * `withTraceContext(tc, () => proxy.Method())` is covered).
 *
 * @throws ArgumentError when the `traceparent` breaks W3C Trace Context §3.2, or when there is neither a `traceparent`
 * nor a `baggage`.
 */
export function withTraceContext<T>(traceContext: TraceContext, callback: () => T): T {
    assertArgument({ traceContext }, 'object');

    return TraceContextRuntime.overrides.run(TraceHeaders.validated(traceContext), callback);
}

/**
 * The trace context received with the callback being handled, or undefined when none arrived or
 * the policy lacks `TelemetryPropagationPolicy.Incoming`. On Node it is available across the handler's awaits; in the browser
 * only until the handler first awaits.
 */
export function currentIncomingTraceContext(): TraceContext | undefined {
    return TraceContextRuntime.incoming.get();
}
