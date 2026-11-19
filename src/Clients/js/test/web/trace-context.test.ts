import { expect } from 'chai';
import { TelemetryPropagationPolicy, TraceContext, currentIncomingTraceContext, withTraceContext } from '../../src/std';
import { IpcWebImpl } from '../../src/web';
import { ITraceProbe, ITraceProbeCallback } from './Contracts';
import { serverUrl } from './Fixtures';

const traceId = '4bf92f3577b34da6a3ce929d0e0e4736';
const spanId = '00f067aa0ba902b7';
const traceparent = `00-${traceId}-${spanId}-01`;

describe('web:trace context end-to-end', () => {
    const sut = new IpcWebImpl();
    const address = (x: any) => x.isWebSocket(serverUrl);
    let proxy: ITraceProbe = null!;
    let onCallback: () => Promise<string> = async () => '';

    beforeAll(() => {
        sut.config.forAnyAddress().setTelemetryPropagation(TelemetryPropagationPolicy.Both);
        proxy = sut.proxy.withAddress(address).withService(ITraceProbe);
        sut.callback.forAddress(address).forService<ITraceProbeCallback>('ITraceProbeCallback').is({ Probe: () => onCallback() });
    });

    afterAll(async () => {
        await sut.disposeAllChannels();
    });

    it('should send an explicit override', async () => {
        const actual = await withTraceContext({ traceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId=alice,x=a%20b' }, () => proxy.Probe());

        expect(actual).to.deep.include({
            ActivityTraceId: traceId,
            ActivityParentSpanId: spanId,
            ActivityTraceState: 'congo=t61rcWkgMzE',
        });
        expect(actual.ActivityBaggage).to.deep.equal(['userId=alice', 'x=a b']);
    });

    it('should send nothing outside a scope', async () => {
        const actual = await proxy.Probe();

        expect(actual.ActivityTraceId).to.equal(null);
    });

    it("should receive a callback carrying the server's client span", async () => {
        let incoming: TraceContext | undefined;
        onCallback = async () => {
            incoming = currentIncomingTraceContext();
            return 'done';
        };

        const actual = await withTraceContext({ traceparent }, () => proxy.ProbeViaCallback());

        expect(actual.ServerActivityTraceId).to.equal(traceId);
        expect(incoming?.traceparent).to.match(new RegExp(`^00-${traceId}-[0-9a-f]{16}-[0-9a-f]{2}$`));
        expect(incoming!.traceparent!.substring(36, 52)).to.not.equal(actual.ServerActivitySpanId);
    });

    // Reading per policy is covered by the std tests.
    for (const [policy, sends] of [
        [TelemetryPropagationPolicy.None, false],
        [TelemetryPropagationPolicy.Incoming, false],
        [TelemetryPropagationPolicy.Outgoing, true],
    ] as Array<[TelemetryPropagationPolicy, boolean]>) {
        it(`should ${sends ? '' : 'not '}send with ${TelemetryPropagationPolicy[policy]}`, async () => {
            sut.config.forAddress(address).setTelemetryPropagation(policy);
            try {
                const probe = await withTraceContext({ traceparent }, () => proxy.Probe());

                expect(probe).to.deep.include(sends ? { ActivityTraceId: traceId, ActivityParentSpanId: spanId } : { ActivityTraceId: null });
            } finally {
                sut.config.forAddress(address).setTelemetryPropagation(TelemetryPropagationPolicy.Both);
            }
        });
    }
});
