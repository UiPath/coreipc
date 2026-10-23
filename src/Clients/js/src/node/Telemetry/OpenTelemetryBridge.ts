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
        return (load ? load() : require(/* webpackIgnore: true */ '@opentelemetry/api')) as OtelApi;
    } catch {
        return undefined;
    }
}

/* @internal */
export class OpenTelemetryBridge implements TracingBridge {
    private _api: OtelApi | null | undefined;

    // Loaded lazily and synchronously, so the optional peer dependency costs nothing while propagation is off.
    constructor(private readonly _load: () => OtelApi | undefined = loadOpenTelemetry) {}

    public current(): TraceContext | undefined {
        const api = this.api;
        if (!api) {
            return undefined;
        }

        const active = api.context.active();
        const spanContext = api.trace.getSpanContext(active);
        if (!spanContext || !api.trace.isSpanContextValid(spanContext)) {
            return undefined;
        }

        const flags = spanContext.traceFlags.toString(16).padStart(2, '0');
        const baggage = api.propagation.getBaggage(active);
        return TraceHeaders.normalize(
            `00-${spanContext.traceId}-${spanContext.spanId}-${flags}`,
            spanContext.traceState?.serialize(),
            baggage && TraceHeaders.encodeBaggage(baggage.getAllEntries().map(([key, entry]) => [key, entry.value])),
        );
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

        const [, traceId, spanId, flags] = traceContext.traceparent.split('-');
        const spanContext: OtelSpanContext = { traceId, spanId, traceFlags: parseInt(flags, 16), isRemote: true };
        if (traceContext.tracestate) {
            spanContext.traceState = api.createTraceState(traceContext.tracestate);
        }

        let context = api.trace.setSpanContext(api.ROOT_CONTEXT, spanContext);

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
