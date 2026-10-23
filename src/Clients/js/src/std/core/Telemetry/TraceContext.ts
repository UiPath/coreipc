import { assertArgument } from '../../bcl';
import { TraceContextRuntime } from './TraceContextRuntime';
import { TraceHeaders } from './TraceHeaders';

/** The directions in which an `ipc` instance propagates the W3C trace context, see `ipc.config.setTelemetryPropagation`. */
export enum TelemetryPropagationPolicy {
    None = 0,
    /** The callbacks it receives run within the trace context they arrived with (`currentIncomingTraceContext`). */
    Incoming = 1,
    /** The calls it makes carry the ambient trace context (`withTraceContext`, else the active OpenTelemetry span on Node). */
    Outgoing = 2,
    Both = 3,
}

/**
 * The W3C trace context of an IPC call, propagated only in the directions `ipc.config.setTelemetryPropagation` allows.
 */
export interface TraceContext {
    traceparent: string;
    tracestate?: string;
    /** W3C Baggage format: comma-separated, percent-encoded key=value members. */
    baggage?: string;
}

/**
 * Runs `callback` with `traceContext` as the trace context of every outgoing call made within it
 * (including calls made by a callback handler), winning over the ambient OpenTelemetry context
 * (read on Node only; the browser package never reads it).
 * Scopes nest; the previous one is restored when `callback` returns.
 *
 * On Node the scope follows the awaits inside `callback`. In the browser it is synchronous: it only applies
 * to calls started before `callback` first awaits (a proxy call builds its request synchronously, so
 * `withTraceContext(tc, () => proxy.Method())` is covered).
 *
 * @throws ArgumentError when a value breaks its W3C rule: `tracestate` at most 32 list members,
 * `baggage` at most 64 list members and 8192 UTF-8 bytes.
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
