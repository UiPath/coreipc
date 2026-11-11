import { TraceContext, TraceHeaders, TracingBridge } from '../../std';

// The slice of @opentelemetry/api used here, declared locally so its types never reach our public typings.
interface OtelTraceState {
    serialize(): string;
}

interface OtelSpanContext {
    traceId: string;
    spanId: string;
    traceFlags: number;
    isRemote?: boolean;
    traceState?: OtelTraceState;
}

interface OtelBaggage {
    getAllEntries(): Array<[string, { value: string }]>;
}

/* @internal */
export interface OtelApi {
    ROOT_CONTEXT: unknown;
    context: {
        active(): unknown;
        with<R>(context: unknown, callback: () => R): R;
    };
    trace: {
        getSpanContext(context: unknown): OtelSpanContext | undefined;
        setSpanContext(context: unknown, spanContext: OtelSpanContext): unknown;
        isSpanContextValid(spanContext: OtelSpanContext): boolean;
    };
    propagation: {
        getBaggage(context: unknown): OtelBaggage | undefined;
        setBaggage(context: unknown, baggage: unknown): unknown;
        createBaggage(entries: { [key: string]: { value: string } }): unknown;
    };
    createTraceState(rawTraceState?: string): OtelTraceState;
}

/* @internal */
export function loadOpenTelemetry(load?: () => unknown): OtelApi | undefined {
    try {
        // A literal require written inside this try, so a consumer's bundler treats the optional peer as optional.
        const api: unknown = load ? load() : require(/* webpackIgnore: true */ '@opentelemetry/api');
        return isOtelApi(api) ? api : undefined;
    } catch {
        return undefined;
    }
}

// A bundler alias to false gives an empty module, and a stub or an API older than 1.1 lacks parts: neither counts as installed.
function isOtelApi(api: any): api is OtelApi {
    return (
        api?.ROOT_CONTEXT !== undefined &&
        typeof api.context?.active === 'function' &&
        typeof api.context.with === 'function' &&
        typeof api.trace?.getSpanContext === 'function' &&
        typeof api.trace.setSpanContext === 'function' &&
        typeof api.trace.isSpanContextValid === 'function' &&
        typeof api.propagation?.getBaggage === 'function' &&
        typeof api.propagation.setBaggage === 'function' &&
        typeof api.propagation.createBaggage === 'function' &&
        typeof api.createTraceState === 'function'
    );
}

// The tracestate each received TraceState was made from, so a callback passes it on as received, as .NET and Python do,
// rather than as OpenTelemetry writes it again (repeated keys merged, nothing over 512 characters).
const receivedTraceStates = new WeakMap<object, string>();

/* @internal */
export class OpenTelemetryBridge implements TracingBridge {
    private _api: OtelApi | null | undefined;

    // Loaded lazily and synchronously, on the first call with Outgoing or the first callback, so an app with neither never loads it.
    constructor(private readonly _load: () => OtelApi | undefined = loadOpenTelemetry) {}

    public current(): TraceContext | undefined {
        const api = this.api;
        if (!api) {
            return undefined;
        }

        // OpenTelemetry keeps what plain JS gives it unchecked. A baggage value String() can't convert is dropped (encodeBaggage).
        // A span or baggage that can't be read at all, such as a span context without traceFlags, gives no trace context, as an
        // API that fails to load does, rather than fail the call.
        try {
            const active = api.context.active();
            const spanContext = api.trace.getSpanContext(active);
            const baggage = api.propagation.getBaggage(active);
            // A missing entry goes empty (k=), as a missing value does.
            const encodedBaggage = baggage && TraceHeaders.encodeBaggage(baggage.getAllEntries().map(([key, entry]) => [key, entry?.value]));
            if (!spanContext || !api.trace.isSpanContextValid(spanContext)) {
                return encodedBaggage ? TraceHeaders.normalize(undefined, undefined, encodedBaggage) : undefined;
            }

            const flags = spanContext.traceFlags.toString(16).padStart(2, '0');
            const tracestate = spanContext.traceState && (receivedTraceStates.get(spanContext.traceState) ?? spanContext.traceState.serialize());
            // A TraceState from plain JS may write something that isn't text.
            if (tracestate && typeof tracestate !== 'string') {
                return undefined;
            }
            return TraceHeaders.normalize(`00-${spanContext.traceId}-${spanContext.spanId}-${flags}`, tracestate, encodedBaggage);
        } catch {
            return undefined;
        }
    }

    public runWithRemoteParent<R>(traceContext: TraceContext | undefined, callback: () => R): R {
        const api = this.api;
        if (!api) {
            return callback();
        }
        // ROOT, never the active context: the handler may run in the async context captured when the connection opened.
        if (!traceContext) {
            return api.context.with(api.ROOT_CONTEXT, callback);
        }

        let context = api.ROOT_CONTEXT;
        if (traceContext.traceparent) {
            const [, traceId, spanId, flags] = traceContext.traceparent.split('-');
            const spanContext: OtelSpanContext = { traceId, spanId, traceFlags: parseInt(flags, 16), isRemote: true };
            if (traceContext.tracestate) {
                spanContext.traceState = api.createTraceState(traceContext.tracestate);
                receivedTraceStates.set(spanContext.traceState, traceContext.tracestate);
            }
            context = api.trace.setSpanContext(context, spanContext);
        }

        if (traceContext.baggage) {
            // A null prototype keeps an untrusted "__proto__" key an ordinary entry.
            const entries: { [key: string]: { value: string } } = Object.create(null);
            for (const [key, value] of TraceHeaders.decodeBaggage(traceContext.baggage)) {
                entries[key] = { value };
            }
            context = api.propagation.setBaggage(context, api.propagation.createBaggage(entries));
        }

        return api.context.with(context, callback);
    }

    private get api(): OtelApi | undefined {
        if (this._api === undefined) {
            this._api = this._load() ?? null;
        }
        return this._api ?? undefined;
    }
}
