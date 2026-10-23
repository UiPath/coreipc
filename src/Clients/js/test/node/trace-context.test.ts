import { expect } from 'chai';
import { context, createTraceState, propagation, ROOT_CONTEXT, trace, INVALID_SPAN_CONTEXT, TraceFlags } from '@opentelemetry/api';
import { AsyncLocalStorageContextManager } from '@opentelemetry/context-async-hooks';

import {
    ArgumentError,
    PromisePal,
    RemoteError,
    TelemetryPropagationPolicy,
    TraceContext,
    TraceContextRuntime,
    TraceHeaders,
    currentIncomingTraceContext,
    withTraceContext,
} from '../../src/std';
import { IpcNodeImpl } from '../../src/node';
import { OpenTelemetryBridge, OtelApi, loadOpenTelemetry } from '../../src/node/Telemetry';
import { ITraceProbe, ITraceProbeCallback } from './Contracts';
import { AddressHelper as TestContext } from './Fixtures';

const traceId = '4bf92f3577b34da6a3ce929d0e0e4736';
const spanId = '00f067aa0ba902b7';
const traceparent = `00-${traceId}-${spanId}-01`;

function otelContext(params: { traceState?: string; baggage?: { [key: string]: string } } = {}) {
    let result = trace.setSpanContext(ROOT_CONTEXT, {
        traceId,
        spanId,
        traceFlags: TraceFlags.SAMPLED,
        traceState: params.traceState ? createTraceState(params.traceState) : undefined,
    });
    if (params.baggage) {
        const entries = Object.fromEntries(Object.entries(params.baggage).map(([key, value]) => [key, { value }]));
        result = propagation.setBaggage(result, propagation.createBaggage(entries));
    }
    return result;
}

function outgoingHeadersFromFakeOtel(sent: TraceContext) {
    const [, fakeTraceId, fakeSpanId, flags] = sent.traceparent.split('-');
    const api: OtelApi = {
        ROOT_CONTEXT,
        context: { active: () => ROOT_CONTEXT, with: context.with.bind(context) },
        trace: {
            getSpanContext: () => ({
                traceId: fakeTraceId,
                spanId: fakeSpanId,
                traceFlags: parseInt(flags, 16),
                traceState: sent.tracestate === undefined ? undefined : { serialize: () => sent.tracestate! },
            }),
            setSpanContext: trace.setSpanContext.bind(trace),
            isSpanContextValid: () => true,
        },
        propagation: {
            getBaggage: () =>
                sent.baggage === undefined
                    ? undefined
                    : { getAllEntries: () => TraceHeaders.decodeBaggage(sent.baggage!).map(([key, value]): [string, { value: string }] => [key, { value }]) },
            setBaggage: propagation.setBaggage.bind(propagation),
            createBaggage: propagation.createBaggage.bind(propagation),
        },
        createTraceState,
    };

    const previous = TraceContextRuntime.bridge;
    TraceContextRuntime.bridge = new OpenTelemetryBridge(() => api);
    try {
        return TraceContextRuntime.getOutgoingHeaders();
    } finally {
        TraceContextRuntime.bridge = previous;
    }
}

function moduleNotFound(name: string): Error {
    return Object.assign(new Error(`Cannot find module '${name}'`), { code: 'MODULE_NOT_FOUND' });
}

describe('node:trace context', () => {
    let contextManager: AsyncLocalStorageContextManager;

    beforeAll(() => {
        contextManager = new AsyncLocalStorageContextManager().enable();
        context.setGlobalContextManager(contextManager);
    });

    afterAll(() => {
        context.disable();
    });

    describe('OpenTelemetry bridge', () => {
        it('should load the installed @opentelemetry/api', () => {
            expect(loadOpenTelemetry()).to.equal(require('@opentelemetry/api'));
        });

        it('should build the three values from the active span context and baggage', () => {
            const actual = context.with(
                otelContext({ traceState: 'congo=t61rcWkgMzE', baggage: { userId: 'alice', x: 'a b' } }),
                () => new OpenTelemetryBridge().current(),
            );

            expect(actual).to.deep.equal({ traceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId=alice,x=a%20b' });
        });

        it('should produce nothing without a valid span context', () => {
            const bridge = new OpenTelemetryBridge();

            expect(bridge.current()).to.equal(undefined);
            expect(context.with(trace.setSpanContext(ROOT_CONTEXT, INVALID_SPAN_CONTEXT), () => bridge.current())).to.equal(undefined);
        });

        it('should send an uppercase trace-id from the tracing library as it is', () => {
            const sent = { traceparent: `00-${traceId.toUpperCase()}-${spanId}-01` };
            expect(outgoingHeadersFromFakeOtel(sent)).to.deep.equal(sent);
        });

        const members = (count: number) => Array.from({ length: count }, (_, i) => `k${i}=v`).join(',');
        for (const [name, header, sent] of [
            ['33 tracestate members', 'tracestate', { traceparent, tracestate: members(33) }],
            ['65 baggage members', 'baggage', { traceparent, baggage: members(65) }],
            ['a baggage over 8192 bytes', 'baggage', { traceparent, baggage: `k=${'v'.repeat(8191)}` }],
        ] as Array<[string, string, TraceContext]>) {
            it(`should send ${name} from the tracing library as it is, though withTraceContext rejects it`, () => {
                expect(outgoingHeadersFromFakeOtel(sent)).to.deep.equal(sent);
                expect(() => withTraceContext(sent, () => 0))
                    .to.throw(ArgumentError)
                    .with.property('paramName', header);
            });
        }

        it('should run the handler within a remote, non-recording span context with the baggage', () => {
            const seen = new OpenTelemetryBridge().runWithRemoteParent(
                { traceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId=alice,x=a%20b' },
                () => ({
                    spanContext: trace.getSpanContext(context.active()),
                    recording: trace.getSpan(context.active())?.isRecording(),
                    baggage: propagation.getBaggage(context.active())?.getAllEntries().map(([key, entry]) => `${key}=${entry.value}`),
                }),
            );

            expect(seen.spanContext).to.deep.include({ traceId, spanId, traceFlags: 1, isRemote: true });
            expect(seen.spanContext?.traceState?.serialize()).to.equal('congo=t61rcWkgMzE');
            expect(seen.recording).to.equal(false);
            expect(seen.baggage).to.deep.equal(['userId=alice', 'x=a b']);
        });

        it('should run a handler that received nothing outside any span, not in the active one', () => {
            const seen = context.with(otelContext({ baggage: { userId: 'alice' } }), () =>
                new OpenTelemetryBridge().runWithRemoteParent(undefined, () => ({
                    spanContext: trace.getSpanContext(context.active()),
                    baggage: propagation.getBaggage(context.active()),
                })),
            );

            expect(seen).to.deep.equal({ spanContext: undefined, baggage: undefined });
        });

        it('should treat any failure to load @opentelemetry/api as not installed', () => {
            for (const error of [
                moduleNotFound('@opentelemetry/api'),
                Object.assign(new Error('@uipath/coreipc tried to access @opentelemetry/api (a peer dependency), which is not provided'), {
                    code: 'MODULE_NOT_FOUND',
                }),
                moduleNotFound('some-dependency-of-the-api'),
                new SyntaxError('Unexpected token'),
            ]) {
                expect(loadOpenTelemetry(() => { throw error; })).to.equal(undefined);
            }
        });

        it('should fall back to nothing when @opentelemetry/api is not installed', () => {
            const bridge = new OpenTelemetryBridge(() =>
                loadOpenTelemetry(() => {
                    throw moduleNotFound('@opentelemetry/api');
                }),
            );

            expect(context.with(otelContext(), () => bridge.current())).to.equal(undefined);
            expect(bridge.runWithRemoteParent({ traceparent }, () => 7)).to.equal(7);
        });

        it('should try loading @opentelemetry/api only once, even when it fails', () => {
            let loads = 0;
            const bridge = new OpenTelemetryBridge(() =>
                loadOpenTelemetry(() => {
                    loads++;
                    throw new Error('broken install');
                }),
            );

            expect(context.with(otelContext(), () => bridge.current())).to.equal(undefined);
            expect(bridge.runWithRemoteParent({ traceparent }, () => 7)).to.equal(7);
            expect(bridge.current()).to.equal(undefined);
            expect(loads).to.equal(1);
        });
    });

    for (const testContext of [TestContext.WebSocket, TestContext.NamedPipe]) {
        describe(`end-to-end with context: ${testContext}`, () => {
            const sut = new IpcNodeImpl();
            let proxy: ITraceProbe = null!;
            let onCallback: () => Promise<string> = async () => '';

            beforeAll(async () => {
                sut.config.setTelemetryPropagation(TelemetryPropagationPolicy.Both);
                proxy = sut.proxy.withAddress(testContext.address).withService(ITraceProbe);
                sut.callback
                    .forAddress(testContext.address)
                    .forService<ITraceProbeCallback>('ITraceProbeCallback')
                    .is({ Probe: () => onCallback() });

                // Callbacks then arrive in the async context of this span.
                await context.with(otelContext(), () => proxy.Probe());
            });

            afterAll(async () => {
                await sut.disposeAllChannels();
            });

            it('should send an explicit override, across awaits', async () => {
                const actual = await withTraceContext({ traceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId=alice,x=a%20b' }, async () => {
                    await PromisePal.delay(1);
                    return await proxy.Probe();
                });

                expect(actual).to.deep.include({
                    TraceParent: traceparent,
                    TraceState: 'congo=t61rcWkgMzE',
                    Baggage: 'userId=alice,x=a%20b',
                    ActivityTraceId: traceId,
                    ActivityParentSpanId: spanId,
                });
                expect(actual.ActivityBaggage).to.deep.equal(['userId=alice', 'x=a b']);
            });

            it('should send the active OpenTelemetry span', async () => {
                const actual = await context.with(otelContext({ baggage: { userId: 'alice' } }), () => proxy.Probe());

                expect(actual).to.deep.include({ TraceParent: traceparent, Baggage: 'userId=alice', ActivityTraceId: traceId, ActivityParentSpanId: spanId });
            });

            it('should let the explicit override win over the active span', async () => {
                const other = '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00';
                const actual = await context.with(otelContext(), () => withTraceContext({ traceparent: other }, () => proxy.Probe()));

                expect(actual.TraceParent).to.equal(other);
            });

            it('should send nothing without an ambient trace context', async () => {
                const actual = await proxy.Probe();

                expect(actual).to.deep.include({ TraceParent: null, TraceState: null, Baggage: null });
            });

            it("should run callbacks within the server's client span, and not leak the override into their calls", async () => {
                let seen: { incoming?: TraceContext; otelSpanId?: string; otelTraceId?: string; remote?: boolean; userId?: string; nestedParent?: string | null } = {};
                onCallback = async () => {
                    const incoming = currentIncomingTraceContext();
                    await PromisePal.delay(1);
                    const spanContext = trace.getSpanContext(context.active());
                    seen = {
                        incoming,
                        otelSpanId: spanContext?.spanId,
                        otelTraceId: spanContext?.traceId,
                        remote: spanContext?.isRemote,
                        userId: propagation.getBaggage(context.active())?.getEntry('userId')?.value,
                        nestedParent: (await proxy.Probe()).ActivityParentSpanId,
                    };
                    return 'done';
                };

                const actual = await withTraceContext({ traceparent, baggage: 'userId=alice' }, () => proxy.ProbeViaCallback());

                expect(actual.Callback).to.equal('done');
                expect(actual.ServerActivityTraceId).to.equal(traceId);
                expect(seen.incoming?.traceparent).to.match(new RegExp(`^00-${traceId}-[0-9a-f]{16}-[0-9a-f]{2}$`));
                const clientSpanId = seen.incoming!.traceparent.substring(36, 52);
                expect(clientSpanId).to.not.equal(actual.ServerActivitySpanId);
                expect(seen.incoming?.baggage).to.equal('userId=alice');
                expect(seen).to.deep.include({ otelSpanId: clientSpanId, otelTraceId: traceId, remote: true, userId: 'alice' });
                expect(seen.nestedParent).to.equal(clientSpanId);
            });

            it('should not leak the span that opened the connection into a callback that arrived without a trace context', async () => {
                let seen: { otelSpanId?: string; nestedTraceParent?: string | null } = {};
                onCallback = async () => {
                    await PromisePal.delay(1);
                    seen = {
                        otelSpanId: trace.getSpanContext(context.active())?.spanId,
                        nestedTraceParent: (await proxy.Probe()).TraceParent,
                    };
                    return 'done';
                };

                const actual = await proxy.ProbeViaCallback();

                expect(actual).to.deep.include({ Callback: 'done', ServerActivityTraceId: null });
                expect(seen).to.deep.equal({ otelSpanId: undefined, nestedTraceParent: null });
            });

            it('should expose exactly the values a callback arrived with', async () => {
                const callbackTraceparent = '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01';
                onCallback = async () => {
                    const incoming = currentIncomingTraceContext();
                    return `${incoming?.traceparent}|${incoming?.tracestate}|${incoming?.baggage}|${trace.getSpanContext(context.active())?.spanId}`;
                };

                const actual = await proxy.ProbeViaCallbackWithTraceContext(callbackTraceparent, 'a=b', 'k=v');

                expect(actual.Callback).to.equal(`${callbackTraceparent}|a=b|k=v|b7ad6b7169203331`);
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

            it('should ignore a received trace context with Outgoing alone, and not leak the span that opened the connection', async () => {
                sut.config.setTelemetryPropagation(TelemetryPropagationPolicy.Outgoing);
                try {
                    let seen: { incoming?: TraceContext; otelSpanId?: string; nestedTraceParent?: string | null } = {};
                    onCallback = async () => {
                        await PromisePal.delay(1);
                        seen = {
                            incoming: currentIncomingTraceContext(),
                            otelSpanId: trace.getSpanContext(context.active())?.spanId,
                            nestedTraceParent: (await proxy.Probe()).TraceParent,
                        };
                        return 'done';
                    };

                    const actual = await proxy.ProbeViaCallbackWithTraceContext('00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01', null, null);

                    expect(actual.Callback).to.equal('done');
                    expect(seen).to.deep.equal({ incoming: undefined, otelSpanId: undefined, nestedTraceParent: null });
                } finally {
                    sut.config.setTelemetryPropagation(TelemetryPropagationPolicy.Both);
                }
            });

            it('should surface the server rejecting an invalid trace context', async () => {
                let error: unknown;
                try {
                    await proxy.ProbeViaCallbackWithTraceContext(traceparent, null, 'k=v,'.repeat(65));
                } catch (err) {
                    error = err;
                }

                expect(error).to.be.instanceOf(RemoteError);
                expect(`${(error as RemoteError).message}`).to.contain('System.ArgumentException');
            });
        });
    }
});
