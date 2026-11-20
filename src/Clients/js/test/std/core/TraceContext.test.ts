import { Observer } from 'rxjs';
import { expect } from 'chai';

import {
    Address,
    ArgumentOutOfRangeError,
    CallbackStoreImpl,
    CancellationToken,
    ChannelManager,
    ConfigStore,
    ConnectHelper,
    ContractStore,
    IMessageStream,
    IpcBaseImpl,
    IRpcChannel,
    IRpcChannelFactory,
    JsonConvert,
    RequestHeaders,
    RpcCallContext,
    RpcMessage,
    Socket,
    TelemetryPropagationPolicy,
    TimeSpan,
    Trace,
    TraceContext,
    TraceContextRuntime,
    TraceHeaders,
    currentIncomingTraceContext,
    withTraceContext,
} from '../../../src/std';
import { MockServiceProvider } from '../../infrastructure';

const traceparent = '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01';
const otherTraceparent = '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00';
const members = (count: number) => Array.from({ length: count }, (_, i) => `k${i}=v`).join(',');
/** `members(count)` as .NET's W3C propagator writes it. */
const encodedMembers = (count: number) => Array.from({ length: count }, (_, i) => `k${i} = v`).join(', ');

class MockAddress extends Address {
    get key() {
        return 'mock:trace';
    }
    async connect(helper: ConnectHelper, timeout: TimeSpan, ct: CancellationToken): Promise<Socket> {
        throw new Error('Should not be called in unit tests');
    }
}

class CapturingChannel implements IRpcChannel {
    readonly requests: RpcMessage.Request[] = [];
    readonly isDisposed = false;
    async disposeAsync(): Promise<void> {}
    async call(request: RpcMessage.Request): Promise<RpcMessage.Response> {
        this.requests.push(request);
        return new RpcMessage.Response(request.Id, JSON.stringify(this.requests.length - 1), null);
    }
}

class CapturingChannelFactory implements IRpcChannelFactory {
    readonly channel = new CapturingChannel();
    observer: Observer<RpcCallContext.Incomming> | undefined;

    create(
        address: Address,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        observer: Observer<RpcCallContext.Incomming>,
        messageStreamFactory?: IMessageStream.Factory,
    ): IRpcChannel {
        this.observer = observer;
        return this.channel;
    }
}

class IService {
    Do(): Promise<number> {
        throw void 0;
    }
}

class OtherAddress extends MockAddress {
    get key() {
        return 'mock:other';
    }
}

function createConfigStore() {
    return new ConfigStore<any>(new MockServiceProvider<any>({ implementation: { contractStore: new ContractStore() } }));
}

/** `policy` is set for any address; `addressPolicy`, when given, for the address the calls and callbacks use. */
function createSut(policy: TelemetryPropagationPolicy, addressPolicy?: TelemetryPropagationPolicy) {
    const contractStore = new ContractStore();
    const callbackStore = new CallbackStoreImpl();
    const configStore = new ConfigStore<any>(new MockServiceProvider<any>({ implementation: { contractStore } }));
    configStore.setTelemetryPropagation(undefined, policy);
    const sp = new MockServiceProvider<any>({ implementation: { configStore, contractStore, callbackStore } });
    const factory = new CapturingChannelFactory();
    const address = new MockAddress();
    if (addressPolicy !== undefined) {
        configStore.setTelemetryPropagation(address, addressPolicy);
    }
    const channelManager = new ChannelManager(sp, address, factory);

    return {
        factory,
        async call(): Promise<RpcMessage.Request> {
            const index = (await channelManager.invokeMethod(IService, 'Do', [])) as number;
            return factory.channel.requests[index];
        },
        async receiveCallback(headers: unknown, handler: () => unknown): Promise<RpcMessage.Response> {
            callbackStore.set('ICallback', address, { Probe: handler });
            if (!factory.observer) {
                await channelManager.invokeMethod(IService, 'Do', []);
            }
            const request = Object.assign(new RpcMessage.Request(0, 'ICallback', 'Probe', []), { Id: '7', Headers: headers });
            let response: RpcMessage.Response | undefined;
            await factory.observer!.next(
                new RpcCallContext.Incomming(request, async r => {
                    response = r;
                }),
            );
            return response!;
        },
    };
}

const invalidTraceparents: Array<[string, string]> = [
    ['garbage', 'garbage'],
    ['uppercase hex', '00-4BF92F3577B34DA6A3CE929D0E0E4736-00f067aa0ba902b7-01'],
    ['an all-zero trace id', '00-00000000000000000000000000000000-00f067aa0ba902b7-01'],
    ['an all-zero span id', '00-4bf92f3577b34da6a3ce929d0e0e4736-0000000000000000-01'],
    ['version ff', 'ff-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01'],
    ['version 00 with an extra field', '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-extra'],
    ['a higher version without a dash after 55 characters', '01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01extra'],
    ['a higher version shorter than 55 characters', '01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-0'],
];

// The baggage as the real bridge returns it: already encoded.
const ambientTraceContext: TraceContext = { traceparent: otherTraceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'ambient = 1' };

/** Runs `act` with a tracing bridge whose ambient trace context is `ambientTraceContext`. */
async function withAmbientTraceContext<T>(act: () => Promise<T>, ambient: TraceContext = ambientTraceContext): Promise<T> {
    const previous = TraceContextRuntime.bridge;
    TraceContextRuntime.bridge = {
        current: () => ambient,
        runWithRemoteParent: (_, callback) => callback(),
    };
    try {
        return await act();
    } finally {
        TraceContextRuntime.bridge = previous;
    }
}

describe('trace context', () => {
    describe('withTraceContext', () => {
        const run = (traceContext: TraceContext) => withTraceContext(traceContext, () => 1);

        it('should return what the callback returns', () => {
            expect(withTraceContext({ traceparent }, () => 42)).to.equal(42);
        });

        it('should accept values over the W3C limits', () => {
            expect(run({ traceparent, tracestate: members(33), baggage: members(65) })).to.equal(1);
        });

        const outgoing = (traceContext: TraceContext) => withTraceContext(traceContext, () => TraceContextRuntime.getOutgoingHeaders());

        for (const [name, value] of invalidTraceparents) {
            it(`should leave out a traceparent with ${name}, and its tracestate, without throwing`, () => {
                expect(outgoing({ traceparent: value, tracestate: 'a=b' })).to.equal(undefined);
                expect(outgoing({ traceparent: value, tracestate: 'a=b', baggage: 'k=v' })).to.deep.equal({ baggage: 'k = v' });
            });
        }

        it('should accept a context without a traceparent', () => {
            expect(run({ baggage: 'k=v' })).to.equal(1);
        });

        for (const [name, value] of [
            ['an empty context', {}],
            ['empty values', { traceparent: '', tracestate: '', baggage: '' }],
            ['a tracestate alone', { tracestate: 'a=b' }],
            ['a baggage with no valid member', { baggage: 'garbage' }],
        ] as Array<[string, TraceContext]>) {
            it(`should run ${name}, which leaves neither a traceparent nor a baggage, as without this scope`, () => {
                expect(withTraceContext({ traceparent }, () => outgoing(value))).to.deep.equal({ traceparent });
                expect(outgoing(value)).to.equal(undefined);
            });
        }

        it('should run a missing trace context, as currentIncomingTraceContext() gives when none arrived, as without this scope', () => {
            for (const missing of [undefined, null!]) {
                expect(withTraceContext({ traceparent }, () => withTraceContext(missing, () => TraceContextRuntime.getOutgoingHeaders()))).to.deep.equal({
                    traceparent,
                });
                expect(withTraceContext(missing, () => 1)).to.equal(1);
            }
        });

        it('should reject a trace context that is not an object', () => {
            expect(() => withTraceContext('x' as unknown as TraceContext, () => 1)).to.throw();
        });

        it('should say once, through Trace, what a scope leaves out', () => {
            const logged: unknown[] = [];
            const listener = Trace.addListener(entry => logged.push(entry));
            try {
                TraceHeaders.resetLeftOutLogged();
                outgoing({ traceparent });
                expect(logged).to.deep.equal([]);
                outgoing({ baggage: 'garbage' });
                outgoing({ baggage: 'garbage' });
                expect(logged).to.deep.equal(["A withTraceContext scope left out what it couldn't use: the baggage has no member it can send."]);
                logged.length = 0;
                TraceHeaders.resetLeftOutLogged();
                outgoing({ tracestate: 'a=b' });
                expect(logged).to.deep.equal(["A withTraceContext scope left out what it couldn't use: the tracestate goes only with a traceparent."]);
                logged.length = 0;
                TraceHeaders.resetLeftOutLogged();
                outgoing({ traceparent: 'garbage', baggage: 5 } as unknown as TraceContext);
                outgoing({ traceparent: 'garbage' });
                expect(logged).to.deep.equal([
                    "A withTraceContext scope left out what it couldn't use: the traceparent isn't a W3C traceparent, the baggage isn't text.",
                ]);
            } finally {
                listener.dispose();
            }
        });

        it("should leave out a value that isn't text, without throwing", () => {
            expect(outgoing({ baggage: 5 } as unknown as TraceContext)).to.equal(undefined);
            expect(outgoing({ traceparent, tracestate: 7 } as unknown as TraceContext)).to.deep.equal({ traceparent });
            expect(outgoing({ traceparent: 1, baggage: 'k=v' } as unknown as TraceContext)).to.deep.equal({ baggage: 'k = v' });
        });
    });

    describe('outgoing calls', () => {
        it('should send no Headers with the None policy, even within an override', async () => {
            const sut = createSut(TelemetryPropagationPolicy.None);
            const request = await withTraceContext({ traceparent }, () => sut.call());

            expect('Headers' in request).to.equal(false);
            expect(JSON.stringify(request)).not.to.contain('Headers');
        });

        it('should send no Headers without an ambient trace context', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await sut.call();

            expect('Headers' in request).to.equal(false);
            expect(Object.keys(JSON.parse(JSON.stringify(request)))).to.deep.equal(['TimeoutInSeconds', 'Endpoint', 'MethodName', 'Parameters', 'Id']);
        });

        it('should send the override as .NET writes it', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ traceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId=alice' }, () => sut.call());

            expect(request.Headers).to.deep.equal({ traceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId = alice' });
            expect(JSON.stringify(request)).to.contain(
                `"Headers":{"traceparent":"${traceparent}","tracestate":"congo=t61rcWkgMzE","baggage":"userId = alice"}`,
            );
        });

        for (const future of ['01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-future', '01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-é']) {
            it(`should send the higher-version traceparent ${future} as version 00`, async () => {
                const sut = createSut(TelemetryPropagationPolicy.Both);
                const request = await withTraceContext({ traceparent: future }, () => sut.call());

                expect(request.Headers).to.deep.equal({ traceparent });
            });
        }

        it('should not send an ambient traceparent with an all-zero id, nor its tracestate', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withAmbientTraceContext(() => sut.call(), {
                traceparent: '00-00000000000000000000000000000000-00f067aa0ba902b7-01',
                tracestate: 'a=b',
                baggage: 'k = v',
            });

            expect(request.Headers).to.deep.equal({ baggage: 'k = v' });
        });

        it('should omit empty tracestate and baggage', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ traceparent, tracestate: '', baggage: '' }, () => sut.call());

            expect(request.Headers).to.deep.equal({ traceparent });
        });

        for (const [name, sent, expected] of [
            ['keep every member, with no count limit', members(40), members(40)],
            ['keep the raw text, spaces around members included, without a trailing comma', ' a=1 ,\tb=2 ,c=3 ,', ' a=1 ,\tb=2 ,c=3 '],
            ['keep repeated keys', 'a=1,b=2,a=3', 'a=1,b=2,a=3'],
            ['stop at the first member that breaks the grammar', 'congo=t61rcWkgMzE,Bad=1,b=2', 'congo=t61rcWkgMzE'],
            ['stop at an empty member', 'a=1,,b=2', 'a=1'],
            ['stop at a member with = first or last, even with spaces after it', 'a=1,b= ,c=2', 'a=1'],
            ['stop at a value with a space inside', 'a=1,b=x y,c=2', 'a=1'],
            ['stop at a value with a tab around it', 'a=1,b=\tx,c=2', 'a=1'],
            ['take a key with a vendor part and a value with any visible ASCII but , and =', 'a1-_*/@v=!"#~', 'a1-_*/@v=!"#~'],
            ['stop at the member that takes it, with its comma, over 256 characters', `a=1,b=${'v'.repeat(250)},c=2`, 'a=1'],
            ['keep a member that ends exactly at 256 characters', `a=1,b=${'v'.repeat(250)}`, `a=1,b=${'v'.repeat(250)}`],
        ]) {
            it(`should ${name} in the tracestate`, async () => {
                const sut = createSut(TelemetryPropagationPolicy.Both);
                const request = await withTraceContext({ traceparent, tracestate: sent }, () => sut.call());

                expect(request.Headers!.tracestate).to.equal(expected);
            });
        }

        for (const tracestate of ['Bad=1,a=1', '=1', 'a=', `a=${'v'.repeat(255)}`]) {
            it(`should omit the tracestate ${tracestate.substring(0, 12)} when its first member is invalid`, async () => {
                const sut = createSut(TelemetryPropagationPolicy.Both);
                const request = await withTraceContext({ traceparent, tracestate }, () => sut.call());

                expect(request.Headers).to.deep.equal({ traceparent });
            });
        }

        it('should send the first 64 baggage members', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ traceparent, baggage: members(65) }, () => sut.call());

            expect(request.Headers!.baggage).to.equal(encodedMembers(64));
        });

        it("should decode the override's baggage and encode it again, dropping what a receiver cannot read", async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const send = async (value: string) => (await withTraceContext({ traceparent, baggage: value }, () => sut.call())).Headers!.baggage;

            expect(await send(' a=1 ,bad key=2,b=50%,c=é,d=x%20y;p=1,a=3,e=%F0%9F%98%80')).to.equal('a = 1, d = x%20y%3Bp=1, e = %F0%9F%98%80');
            expect(await send('a=1,foo,b=2')).to.equal('a = 1');
            expect(await send('foo,a=1')).to.equal(undefined);
        });

        it('should send the baggage alone, without the tracestate, when there is no traceparent', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ tracestate: 'a=b', baggage: 'k=v' }, () => sut.call());

            expect(request.Headers).to.deep.equal({ baggage: 'k = v' });
        });

        it('should send the ambient trace context without an override', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withAmbientTraceContext(() => sut.call());

            expect(request.Headers).to.deep.equal(ambientTraceContext);
        });

        it('should send the ambient traceparent and tracestate with the baggage of an override that has no traceparent', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withAmbientTraceContext(() => withTraceContext({ tracestate: 'ignored=1', baggage: 'userId=alice' }, () => sut.call()));

            expect(request.Headers).to.deep.equal({ traceparent: otherTraceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId = alice' });
        });

        it('should let an override with a traceparent win outright over the ambient trace context', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withAmbientTraceContext(() => withTraceContext({ traceparent, baggage: 'userId=alice' }, () => sut.call()));

            expect(request.Headers).to.deep.equal({ traceparent, baggage: 'userId = alice' });
        });

        it('should let inner scopes win and restore the outer one', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const requests = await withTraceContext({ traceparent }, async () => {
                const inner = withTraceContext({ traceparent: otherTraceparent }, () => sut.call());
                const outer = sut.call();
                return Promise.all([inner, outer]);
            });

            expect(requests[0].Headers?.traceparent).to.equal(otherTraceparent);
            expect(requests[1].Headers?.traceparent).to.equal(traceparent);
            expect('Headers' in (await sut.call())).to.equal(false);
        });

        it('should restore the previous scope when the callback throws', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            expect(() =>
                withTraceContext({ traceparent }, () => {
                    throw new Error('boom');
                }),
            ).to.throw('boom');

            expect('Headers' in (await sut.call())).to.equal(false);
        });
    });

    describe('incoming callbacks', () => {
        it('should expose the received values to the handler', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            let seen: TraceContext | undefined;
            const response = await sut.receiveCallback({ traceparent, tracestate: 'a=b', baggage: 'x=a%20b' }, () => {
                seen = currentIncomingTraceContext();
                return 'ok';
            });

            expect(response.Error).to.equal(null);
            expect(response.Data).to.equal('"ok"');
            expect(seen).to.deep.equal({ traceparent, tracestate: 'a=b', baggage: 'x=a%20b' });
            expect(currentIncomingTraceContext()).to.equal(undefined);
        });

        it('should read only the exact lowercase header names', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const seen = new Array<TraceContext | undefined>();
            await sut.receiveCallback({ TraceParent: traceparent, Baggage: 'k=v' }, () => {
                seen.push(currentIncomingTraceContext());
            });
            await sut.receiveCallback({ traceparent, TRACESTATE: 'a=b', BAGGAGE: 'k=v' }, () => {
                seen.push(currentIncomingTraceContext());
            });

            expect(seen).to.deep.equal([undefined, { traceparent }]);
        });

        it('should ignore the other headers when no traceparent arrived', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            let seen: TraceContext | undefined = { traceparent };
            const response = await sut.receiveCallback({ traceparent: null, tracestate: 'a=b', future: 'x' }, () => {
                seen = currentIncomingTraceContext();
            });

            expect(response.Error).to.equal(null);
            expect(seen).to.equal(undefined);
        });

        for (const [name, value] of invalidTraceparents) {
            it(`should ignore a traceparent with ${name}, and its tracestate`, async () => {
                const sut = createSut(TelemetryPropagationPolicy.Both);
                const seen: Array<TraceContext | undefined> = [];
                await sut.receiveCallback({ traceparent: value, tracestate: 'a=b' }, () => {
                    seen.push(currentIncomingTraceContext());
                });
                await sut.receiveCallback({ traceparent: value, tracestate: 'a=b', baggage: 'k=v' }, () => {
                    seen.push(currentIncomingTraceContext());
                });

                expect(seen).to.deep.equal([undefined, { baggage: 'k=v' }]);
            });
        }

        for (const future of ['01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-future', '01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-é']) {
            it(`should accept the higher-version traceparent ${future} as it is`, async () => {
                const sut = createSut(TelemetryPropagationPolicy.Both);
                let seen: TraceContext | undefined;
                await sut.receiveCallback({ traceparent: future }, () => {
                    seen = currentIncomingTraceContext();
                });

                expect(seen).to.deep.equal({ traceparent: future });
            });
        }

        for (const [received, kept] of [
            [' congo=t61rcWkgMzE ,a=1,Bad=1,b=2', ' congo=t61rcWkgMzE ,a=1'],
            ['a=1,,b=2', 'a=1'],
            ['a=1,x=a=b', 'a=1'],
            ['a=x y,b=1', undefined],
            ['2020-01-01T00:00:00Z,a=1', undefined],
            [members(33), members(33)],
            [`a=1,b=${'v'.repeat(300)}`, 'a=1'],
        ] as Array<[string, string | undefined]>) {
            it(`should cut the received tracestate ${received.substring(0, 24)} as it does on send`, async () => {
                const sut = createSut(TelemetryPropagationPolicy.Both);
                let seen: TraceContext | undefined;
                await sut.receiveCallback({ traceparent, tracestate: received }, () => {
                    seen = currentIncomingTraceContext();
                });

                expect(seen).to.deep.equal(kept === undefined ? { traceparent } : { traceparent, tracestate: kept });
            });
        }

        it('should receive a baggage that arrives without a traceparent', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            let seen: TraceContext | undefined;
            await sut.receiveCallback({ baggage: 'k=v' }, () => {
                seen = currentIncomingTraceContext();
            });

            expect(seen).to.deep.equal({ baggage: 'k=v' });
        });

        for (const baggage of [members(65), `k=${'v'.repeat(8191)}`, 'foo,=x', ' a=1 , foo ,b=2']) {
            it(`should keep the received baggage ${baggage.substring(0, 12)} as it arrived`, async () => {
                const sut = createSut(TelemetryPropagationPolicy.Both);
                let seen: TraceContext | undefined;
                const response = await sut.receiveCallback({ traceparent, baggage }, () => {
                    seen = currentIncomingTraceContext();
                });

                expect(response.Error).to.equal(null);
                expect(seen).to.deep.equal({ traceparent, baggage });
            });
        }

        it('should ignore received values that are not strings', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            let seen: TraceContext | undefined;
            const response = await sut.receiveCallback({ traceparent, tracestate: 42, baggage: ['k=v'] }, () => {
                seen = currentIncomingTraceContext();
            });

            expect(response.Error).to.equal(null);
            expect(seen).to.deep.equal({ traceparent });
        });

        // Parsed as RpcMessage.Request.fromNetwork parses a request. The last three only pin JSON.parse, which keeps the last of
        // a repeated name.
        for (const headers of [
            `{"traceparent":null,"TraceParent":"${traceparent}"}`,
            `{"TraceParent":"${traceparent}","traceparent":null}`,
            `{"traceparent":5,"TraceParent":"${traceparent}"}`,
            `{"TraceParent":null,"TRACEPARENT":"${traceparent}"}`,
            `{"TraceParent":5,"TRACEPARENT":"${traceparent}"}`,
            `{"traceparent":"${traceparent}","traceparent":5}`,
            `{"traceparent":"${traceparent}"},"Headers":5`,
            `{"traceparent":"${traceparent}"},"Headers":null`,
        ]) {
            it(`should read "Headers":${headers} as .NET and Python do`, async () => {
                const sut = createSut(TelemetryPropagationPolicy.Both);
                const request = JsonConvert.deserializeObject(`{"Headers":${headers}}`, RpcMessage.Request);
                let seen: TraceContext | undefined = { traceparent };
                const response = await sut.receiveCallback(request.Headers, () => {
                    seen = currentIncomingTraceContext();
                });

                expect(response.Error).to.equal(null);
                expect(seen).to.equal(undefined);
            });
        }

        it('should read in linear time, even a long run of spaces inside a member', () => {
            const spaces = ' '.repeat(100_000);
            const started = Date.now();

            const received = TraceHeaders.fromHeaders({ traceparent, tracestate: `b=1,a=x${spaces}y`, baggage: `k=x${spaces}y` });
            const decoded = TraceHeaders.decodeBaggage(`k=${spaces}x${spaces}y${spaces}`);

            expect(received).to.deep.equal({ traceparent, tracestate: 'b=1', baggage: `k=x${spaces}y` });
            expect(decoded).to.deep.equal([['k', `x${spaces}y`]]);
            // The regex this replaced took seconds on these.
            expect(Date.now() - started).to.be.below(1000);
        });

        it('should fail the call with an error response when the tracing bridge throws', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const previous = TraceContextRuntime.bridge;
            TraceContextRuntime.bridge = {
                current: () => undefined,
                runWithRemoteParent: () => {
                    throw new Error('broken tracing library');
                },
            };
            try {
                let invoked = false;
                const response = await sut.receiveCallback({ traceparent }, () => {
                    invoked = true;
                });

                expect(invoked).to.equal(false);
                expect(response.RequestId).to.equal('7');
                expect(response.Error?.Message).to.contain('broken tracing library');
            } finally {
                TraceContextRuntime.bridge = previous;
            }
        });

        it('should ignore incoming headers entirely with the None policy', async () => {
            const sut = createSut(TelemetryPropagationPolicy.None);
            let seen: TraceContext | undefined = { traceparent };
            const response = await sut.receiveCallback({ traceparent, baggage: 'k=v' }, () => {
                seen = currentIncomingTraceContext();
            });

            expect(response.Error).to.equal(null);
            expect(seen).to.equal(undefined);
        });

        it('should run the handler isolated from the context it was delivered in, with the None policy too', async () => {
            const sut = createSut(TelemetryPropagationPolicy.None);
            await sut.call();
            let seen: Array<TraceContext | RequestHeaders | undefined> | undefined;
            await TraceContextRuntime.incoming.run({ traceparent }, () =>
                withTraceContext({ traceparent: otherTraceparent }, () =>
                    sut.receiveCallback(undefined, () => {
                        seen = [currentIncomingTraceContext(), TraceContextRuntime.getOutgoingHeaders()];
                    }),
                ),
            );

            expect(seen).to.deep.equal([undefined, undefined]);
        });

        for (const policy of [TelemetryPropagationPolicy.Both, TelemetryPropagationPolicy.Outgoing]) {
            it(`should not apply an outer override to calls made by the handler (${TelemetryPropagationPolicy[policy]})`, async () => {
                const sut = createSut(policy);
                await sut.call();
                let nested: Promise<RpcMessage.Request> | undefined;
                await withTraceContext({ traceparent: otherTraceparent }, () =>
                    sut.receiveCallback(undefined, () => {
                        nested = sut.call();
                    }),
                );

                expect('Headers' in (await nested!)).to.equal(false);
            });
        }
    });

    describe('each direction alone', () => {
        for (const [policy, sends, reads] of [
            [TelemetryPropagationPolicy.None, false, false],
            [TelemetryPropagationPolicy.Incoming, false, true],
            [TelemetryPropagationPolicy.Outgoing, true, false],
            [TelemetryPropagationPolicy.Both, true, true],
        ] as Array<[TelemetryPropagationPolicy, boolean, boolean]>) {
            it(`should ${sends ? '' : 'not '}send and ${reads ? '' : 'not '}read with ${TelemetryPropagationPolicy[policy]}`, async () => {
                const sut = createSut(policy);
                const request = await withTraceContext({ traceparent }, () => sut.call());
                let seen: TraceContext | undefined;
                await sut.receiveCallback({ traceparent: otherTraceparent, baggage: 'k=v' }, () => {
                    seen = currentIncomingTraceContext();
                });

                expect(request.Headers).to.deep.equal(sends ? { traceparent } : undefined);
                expect(seen).to.deep.equal(reads ? { traceparent: otherTraceparent, baggage: 'k=v' } : undefined);
            });
        }
    });

    describe('per address', () => {
        it('should be None by default', () => {
            expect(createConfigStore().getTelemetryPropagation(new MockAddress())).to.equal(TelemetryPropagationPolicy.None);
        });

        it('should fall back to the value set for any address', () => {
            const configStore = createConfigStore();
            configStore.setTelemetryPropagation(undefined, TelemetryPropagationPolicy.Outgoing);
            configStore.setTelemetryPropagation(new OtherAddress(), TelemetryPropagationPolicy.None);

            expect(configStore.getTelemetryPropagation(new MockAddress())).to.equal(TelemetryPropagationPolicy.Outgoing);
            expect(configStore.getTelemetryPropagation(new OtherAddress())).to.equal(TelemetryPropagationPolicy.None);
        });

        for (const [anyAddress, address, sends, reads] of [
            [TelemetryPropagationPolicy.Both, TelemetryPropagationPolicy.None, false, false],
            [TelemetryPropagationPolicy.None, TelemetryPropagationPolicy.Both, true, true],
            [TelemetryPropagationPolicy.Both, TelemetryPropagationPolicy.Incoming, false, true],
        ] as Array<[TelemetryPropagationPolicy, TelemetryPropagationPolicy, boolean, boolean]>) {
            it(`should let ${TelemetryPropagationPolicy[address]} for the address win over ${TelemetryPropagationPolicy[anyAddress]} for any address`, async () => {
                const sut = createSut(anyAddress, address);
                const request = await withTraceContext({ traceparent }, () => sut.call());
                let seen: TraceContext | undefined;
                await sut.receiveCallback({ traceparent: otherTraceparent }, () => {
                    seen = currentIncomingTraceContext();
                });

                expect(request.Headers).to.deep.equal(sends ? { traceparent } : undefined);
                expect(seen).to.deep.equal(reads ? { traceparent: otherTraceparent } : undefined);
            });
        }

        it('should reject a policy that is not a number', () => {
            const config = new IpcBaseImpl.ConfigurationWithAddress(new MockServiceProvider<any>({ implementation: { configStore: createConfigStore() } }));

            expect(() => config.setTelemetryPropagation('Both' as any))
                .to.throw(ArgumentOutOfRangeError)
                .with.property('paramName', 'policy');
        });
    });

    describe('baggage encoding', () => {
        it("should escape every ASCII character outside W3C Baggage's baggage-octet, and '%', as .NET does", () => {
            const isOctet = (code: number) =>
                code !== 0x25 && (code === 0x21 || (code >= 0x23 && code <= 0x2b) || (code >= 0x2d && code <= 0x3a) || (code >= 0x3c && code <= 0x5b) || (code >= 0x5d && code <= 0x7e));
            for (let code = 0; code < 0x80; code++) {
                const char = String.fromCharCode(code);
                const expected = isOctet(code) ? char : `%${code.toString(16).toUpperCase().padStart(2, '0')}`;
                expect(TraceHeaders.encodeBaggage([['k', `a${char}a`]])).to.equal(`k = a${expected}a`, `code ${code}`);
            }
        });

        it('should write members as key = value joined by ", ", trimming keys and values', () => {
            expect(
                TraceHeaders.encodeBaggage([
                    ['userId', 'alice'],
                    [' x\t', " a b,c=d;!'()*é \t"],
                ]),
            ).to.equal("userId = alice, x = a%20b%2Cc=d%3B!'()*%C3%A9");
            expect(TraceHeaders.encodeBaggage([])).to.equal(undefined);
        });

        it('should send a lone surrogate as U+FFFD, as .NET and Python do', () => {
            expect(
                TraceHeaders.encodeBaggage([
                    ['k', 'a\uD800b'],
                    ['pair', '😀'],
                    ['reversed', '\uDE00\uD83D'],
                ]),
            ).to.equal('k = a%EF%BF%BDb, pair = %F0%9F%98%80, reversed = %EF%BF%BD%EF%BF%BD');
        });

        it('should send a key that is a token as it is, and skip any other', () => {
            const keys = [null, '', ' \t', 'bad key', 'k=', 'é', 'k\uDC00', ' k\t', "!#$%&'*+-.^_`|~09AZaz"];
            expect(TraceHeaders.encodeBaggage(keys.map(key => [key as unknown as string, 'v']))).to.equal("k = v, !#$%&'*+-.^_`|~09AZaz = v");
            expect(TraceHeaders.encodeBaggage([['bad key', 'v']])).to.equal(undefined);
        });

        it('should send a null, undefined or empty value as empty, as .NET does', () => {
            for (const value of [null, undefined, '', ' ']) {
                expect(TraceHeaders.encodeBaggage([['empty', value as unknown as string], ['k', 'a,b=c;d']])).to.equal('empty = , k = a%2Cb=c%3Bd');
                expect(TraceHeaders.encodeBaggage([['k', 'v'], ['empty', value as unknown as string]])).to.equal('k = v, empty = ');
            }
        });

        it('should send the first 64 members with a valid key', () => {
            const entries = Array.from({ length: 65 }, (_, i): Array<[string, string]> => [['bad key', 'v'], [`k${i}`, 'v']]).flat();
            expect(TraceHeaders.encodeBaggage(entries)).to.equal(encodedMembers(64));
        });

        it('should stop at the first member that takes the header, with its ", ", to 8192 characters', () => {
            const lone = '\uD800';
            expect(TraceHeaders.encodeBaggage([['k', 'v'.repeat(8185)]])).to.equal(`k = ${'v'.repeat(8185)}`);
            expect(TraceHeaders.encodeBaggage([['k', 'v'.repeat(8186)]])).to.equal(undefined);
            expect(TraceHeaders.encodeBaggage([['a', '1'], ['big', 'v'.repeat(9000)], ['b', '2']])).to.equal('a = 1');
            // A lone surrogate takes 9 characters, as %EF%BF%BD.
            expect(TraceHeaders.encodeBaggage([['a', '1'], ['k', lone.repeat(908)]])).to.equal(`a = 1, k = ${'%EF%BF%BD'.repeat(908)}`);
            expect(TraceHeaders.encodeBaggage([['a', '1'], ['k', lone.repeat(909)]])).to.equal('a = 1');
        });

        it('should decode an encoded , = and ; back exactly', () => {
            const entries: Array<[string, string]> = [
                ['userId', 'alice'],
                ['x', 'a b,c=d;e'],
            ];
            expect(TraceHeaders.decodeBaggage(TraceHeaders.encodeBaggage(entries)!)).to.deep.equal(entries);
        });

        it('should skip a member with a bad key or value, trim, keep properties in the value and the first of a repeated key', () => {
            expect(TraceHeaders.decodeBaggage('a=1, b = 2 ,c=3;ttl=30,bad key=4,d=50%,e=é,f=%zz%41,a=9,g=x')).to.deep.equal([
                ['a', '1'],
                ['b', '2'],
                ['c', '3;ttl=30'],
                ['g', 'x'],
            ]);
        });

        for (const baggage of ['a=1,foo,b=2', 'a=1,k=,b=2', 'a=1,=x,b=2', 'a=1,,b=2', 'a=1,']) {
            it(`should stop reading ${baggage} at a member with no =, or with it first or last`, () => {
                expect(TraceHeaders.decodeBaggage(baggage)).to.deep.equal([['a', '1']]);
            });
        }

        it('should read a value of spaces as empty, and not decode keys', () => {
            expect(TraceHeaders.decodeBaggage('k= ,b=2')).to.deep.equal([
                ['k', ''],
                ['b', '2'],
            ]);
            expect(TraceHeaders.decodeBaggage('k%41=v%41')).to.deep.equal([['k%41', 'vA']]);
            expect(TraceHeaders.decodeBaggage('')).to.deep.equal([]);
        });

        for (const [escaped, decoded] of [
            ['%FF%C3%A9', '\uFFFDé'],
            ['%c3%a9', 'é'],
            ['%C3%28', '\uFFFD('],
            ['%C3%A9%A9', 'é\uFFFD'],
            ['%E2%82%C3%A9', '\uFFFDé'],
            ['%C0%80', '\uFFFD\uFFFD'],
            ['%ED%A0%80', '\uFFFD\uFFFD\uFFFD'],
            ['%F4%90%80%80', '\uFFFD\uFFFD\uFFFD\uFFFD'],
            ['%F0%9F%98%80', '\u{1F600}'],
            ['%EF%BB%BFx', '\uFEFFx'],
            ['x%EF%BB%BFy', 'x\uFEFFy'],
        ]) {
            it(`should decode ${escaped} as TextDecoder does, with U+FFFD for what isn't UTF-8`, () => {
                expect(TraceHeaders.decodeBaggage(`k=${escaped}`)).to.deep.equal([['k', decoded]]);
            });
        }
    });
});
