export interface TraceProbeResult {
    ActivityTraceId: string | null;
    ActivityParentSpanId: string | null;
    ActivitySpanId: string | null;
    ActivityTraceState: string | null;
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
}

export interface ITraceProbeCallback {
    Probe(): Promise<string>;
}
