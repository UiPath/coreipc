import type { TraceContext } from './TraceContext';
import { RequestHeaders, TraceHeaders } from './TraceHeaders';

/* @internal */
export interface AmbientSlot<T> {
    get(): T | undefined;
    run<R>(value: T | undefined, callback: () => R): R;
}

/** Bridges to the platform's idiomatic tracing library (OpenTelemetry on Node). */
/* @internal */
export interface TracingBridge {
    current(): TraceContext | undefined;
    /** Runs `callback` in a fresh tracing context: the remote parent when one arrived, else no span at all. */
    runWithRemoteParent<R>(traceContext: TraceContext | undefined, callback: () => R): R;
}

/* @internal */
export class SynchronousSlot<T> implements AmbientSlot<T> {
    private _value: T | undefined;

    public get(): T | undefined {
        return this._value;
    }

    public run<R>(value: T | undefined, callback: () => R): R {
        const previous = this._value;
        this._value = value;
        try {
            return callback();
        } finally {
            this._value = previous;
        }
    }
}

/* @internal */
export class TraceContextRuntime {
    public static overrides: AmbientSlot<TraceContext> = new SynchronousSlot<TraceContext>();
    public static incoming: AmbientSlot<TraceContext> = new SynchronousSlot<TraceContext>();
    public static bridge: TracingBridge | undefined;

    /** Called once, when a platform entry point loads, before any scope can be open. */
    public static install(createSlot: <T>() => AmbientSlot<T>, bridge?: TracingBridge): void {
        TraceContextRuntime.overrides = createSlot<TraceContext>();
        TraceContextRuntime.incoming = createSlot<TraceContext>();
        TraceContextRuntime.bridge = bridge;
    }

    public static getOutgoingHeaders(): RequestHeaders | undefined {
        const traceContext = TraceContextRuntime.overrides.get() ?? TraceContextRuntime.bridge?.current();
        return traceContext && TraceHeaders.toHeaders(traceContext);
    }

    public static runIncoming<R>(received: TraceContext | undefined, callback: () => R): R {
        // The handler may run in the async context captured when the connection was opened,
        // so an override, incoming value or tracing context active back then must not leak into it.
        return TraceContextRuntime.overrides.run(undefined, () =>
            TraceContextRuntime.incoming.run(received, () =>
                TraceContextRuntime.bridge ? TraceContextRuntime.bridge.runWithRemoteParent(received, callback) : callback(),
            ),
        );
    }
}
