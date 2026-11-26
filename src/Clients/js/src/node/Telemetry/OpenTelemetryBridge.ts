import { Trace, TraceContext, TraceHeaders, TracingBridge } from '../../std';

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
        const api: any = load ? load() : require(/* webpackIgnore: true */ '@opentelemetry/api');
        if (isOtelApi(api)) {
            return api;
        }
        // An empty module is a bundler alias to false, which turns it off on purpose. One with parts of the API (a stub, or
        // a version before 1.1) would otherwise turn propagation off without a word.
        if (api?.context !== undefined || api?.trace !== undefined || api?.propagation !== undefined) {
            Trace.log('@opentelemetry/api loaded but lacks what trace propagation needs (version 1.1 or later), so none is done.');
        }
        return undefined;
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
    private _readFailureLogged = false;

    // Loaded lazily and synchronously, on the first call with Outgoing or the first callback, so an app with neither never loads it.
    constructor(private readonly _load: () => OtelApi | undefined = loadOpenTelemetry) {}

    public current(): TraceContext | undefined {
        const api = this.api;
        if (!api) {
            return undefined;
        }

        // OpenTelemetry keeps what plain JS gives it unchecked. As its own propagators do, each part goes on its own: a span
        // context without traceFlags sends 00, and a part that can't be read is left out, without failing the call. The first
        // time that happens, it says why. A baggage value String() can't convert is dropped by encodeBaggage.
        const active = api.context.active();
        const encodedBaggage = this.read(() => {
            const baggage = api.propagation.getBaggage(active);
            // A missing entry goes empty, as a missing value does.
            return baggage && TraceHeaders.encodeBaggage(baggage.getAllEntries().map(([key, entry]) => [key, entry?.value]));
        });
        const spanContext = this.read(() => {
            const value = api.trace.getSpanContext(active);
            return value && api.trace.isSpanContextValid(value) ? value : undefined;
        });
        const traceparent =
            // OpenTelemetry accepts uppercase hex ids; the traceparent needs lowercase.
            spanContext && this.read(() => `00-${spanContext.traceId.toLowerCase()}-${spanContext.spanId.toLowerCase()}-${Number(spanContext.traceFlags || 0).toString(16).padStart(2, '0')}`);
        const tracestate =
            traceparent &&
            this.read(() => {
                const value = spanContext!.traceState && (receivedTraceStates.get(spanContext!.traceState) ?? spanContext!.traceState.serialize());
                if (value && typeof value !== 'string') {
                    throw new TypeError(`The span context's TraceState wrote a ${typeof value}, not text.`);
                }
                return value;
            });
        return traceparent || encodedBaggage ? TraceHeaders.normalize(traceparent, tracestate, encodedBaggage) : undefined;
    }

    private read<T>(part: () => T): T | undefined {
        try {
            return part();
        } catch (error) {
            if (!this._readFailureLogged) {
                this._readFailureLogged = true;
                // Not `${error}`: plain JS can throw a value that has no text, such as Object.create(null).
                const reason = error instanceof Error ? error.message : typeof error;
                Trace.log(`Part of the OpenTelemetry context couldn't be read, so it isn't sent: ${reason}`);
            }
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
            // A null prototype keeps an untrusted "__proto__" key an ordinary entry. decodeBaggage keeps the last of a repeated key.
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
