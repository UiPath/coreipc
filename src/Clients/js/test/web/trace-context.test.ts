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
        sut.config.setTelemetryPropagation(TelemetryPropagationPolicy.Both);
        proxy = sut.proxy.withAddress(address).withService(ITraceProbe);
        sut.callback.forAddress(address).forService<ITraceProbeCallback>('ITraceProbeCallback').is({ Probe: () => onCallback() });
    });

    afterAll(async () => {
        await sut.disposeAllChannels();
    });

    it('should send an explicit override', async () => {
        const actual = await withTraceContext({ traceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId=alice,x=a%20b' }, () => proxy.Probe());

        expect(actual).to.deep.include({
            TraceParent: traceparent,
            TraceState: 'congo=t61rcWkgMzE',
            Baggage: 'userId=alice,x=a%20b',
            ActivityTraceId: traceId,
            ActivityParentSpanId: spanId,
        });
        expect(actual.ActivityBaggage).to.deep.equal(['userId=alice', 'x=a b']);
    });

    it('should send nothing outside a scope', async () => {
        const actual = await proxy.Probe();

        expect(actual).to.deep.include({ TraceParent: null, TraceState: null, Baggage: null });
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
        expect(incoming!.traceparent.substring(36, 52)).to.not.equal(actual.ServerActivitySpanId);
    });

    it('should expose exactly the values a callback arrived with', async () => {
        const callbackTraceparent = '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01';
        onCallback = async () => {
            const received = currentIncomingTraceContext();
            return `${received?.traceparent}|${received?.tracestate}|${received?.baggage}`;
        };

        const actual = await proxy.ProbeViaCallbackWithTraceContext(callbackTraceparent, 'a=b', 'k=v');

        expect(actual.Callback).to.equal(`${callbackTraceparent}|a=b|k=v`);
    });

    for (const [policy, sends, reads] of [
        [TelemetryPropagationPolicy.None, false, false],
        [TelemetryPropagationPolicy.Incoming, false, true],
        [TelemetryPropagationPolicy.Outgoing, true, false],
    ] as Array<[TelemetryPropagationPolicy, boolean, boolean]>) {
        it(`should ${sends ? '' : 'not '}send and ${reads ? '' : 'not '}read with ${TelemetryPropagationPolicy[policy]}`, async () => {
            const callbackTraceparent = '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01';
            sut.config.setTelemetryPropagation(policy);
            try {
                onCallback = async () => `${currentIncomingTraceContext()?.traceparent}`;

                const probe = await withTraceContext({ traceparent }, () => proxy.Probe());
                const callback = await proxy.ProbeViaCallbackWithTraceContext(callbackTraceparent, null, null);

                expect(probe).to.deep.include(sends ? { TraceParent: traceparent, ActivityParentSpanId: spanId } : { TraceParent: null, TraceState: null, Baggage: null });
                expect(callback.Callback).to.equal(reads ? callbackTraceparent : 'undefined');
            } finally {
                sut.config.setTelemetryPropagation(TelemetryPropagationPolicy.Both);
            }
        });
    }
});
