export interface TraceProbeResult {
    TraceParent: string | null;
    TraceState: string | null;
    Baggage: string | null;
    ActivityTraceId: string | null;
    ActivityParentSpanId: string | null;
    ActivitySpanId: string | null;
    ActivityBaggage: string[] | null;
}

export interface TraceProbeCallbackResult {
    Callback: string | null;
    ServerActivityTraceId: string | null;
    ServerActivitySpanId: string | null;
}

export class ITraceProbe {
    public Probe(): Promise<TraceProbeResult> {
        throw void 0;
    }

    public ProbeViaCallback(): Promise<TraceProbeCallbackResult> {
        throw void 0;
    }

    public ProbeViaCallbackWithTraceContext(
        traceParent: string,
        traceState: string | null,
        baggage: string | null,
    ): Promise<TraceProbeCallbackResult> {
        throw void 0;
    }
}

export interface ITraceProbeCallback {
    Probe(): Promise<string>;
}
