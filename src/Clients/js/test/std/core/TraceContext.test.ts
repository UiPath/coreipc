import { Observer } from 'rxjs';
import { expect } from 'chai';

import {
    Address,
    ArgumentError,
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
    RequestHeaders,
    RpcCallContext,
    RpcMessage,
    Socket,
    TelemetryPropagationPolicy,
    TimeSpan,
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
];

const ambientTraceContext: TraceContext = { traceparent: otherTraceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'ambient=1' };

/** Runs `act` with a tracing bridge whose ambient trace context is `ambientTraceContext`. */
async function withAmbientTraceContext<T>(act: () => Promise<T>): Promise<T> {
    const previous = TraceContextRuntime.bridge;
    TraceContextRuntime.bridge = {
        current: () => ambientTraceContext,
        runWithRemoteParent: (_, callback) => callback(),
    };
    try {
        return await act();
    } finally {
        TraceContextRuntime.bridge = previous;
    }
}

function expectArgumentError(act: () => unknown, paramName: string) {
    try {
        act();
    } catch (error) {
        expect(error).to.be.instanceOf(ArgumentError);
        expect((error as ArgumentError).paramName).to.equal(paramName);
        return;
    }
    expect.fail('Expected an ArgumentError.');
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

        for (const [name, value] of invalidTraceparents) {
            it(`should reject a traceparent with ${name}`, () => {
                expectArgumentError(() => run({ traceparent: value }), 'traceparent');
            });
        }

        it('should accept a context without a traceparent', () => {
            expect(run({ baggage: 'k=v' })).to.equal(1);
        });

        for (const [name, value] of [
            ['an empty context', {}],
            ['empty values', { traceparent: '', tracestate: '', baggage: '' }],
            ['a tracestate alone', { tracestate: 'a=b' }],
        ] as Array<[string, TraceContext]>) {
            it(`should reject ${name}, which has neither a traceparent nor a baggage`, () => {
                expectArgumentError(() => run(value), 'traceContext');
            });
        }

        it('should reject a missing trace context', () => {
            expect(() => withTraceContext(null!, () => 1)).to.throw();
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

        it('should send exactly the override', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ traceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId=alice' }, () => sut.call());

            expect(request.Headers).to.deep.equal({ traceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId=alice' });
            expect(JSON.stringify(request)).to.contain(
                `"Headers":{"traceparent":"${traceparent}","tracestate":"congo=t61rcWkgMzE","baggage":"userId=alice"}`,
            );
        });

        it('should send a higher-version traceparent unchanged', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const future = '01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-future';
            const request = await withTraceContext({ traceparent: future }, () => sut.call());

            expect(request.Headers).to.deep.equal({ traceparent: future });
        });

        it('should omit empty tracestate and baggage', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ traceparent, tracestate: '', baggage: '' }, () => sut.call());

            expect(request.Headers).to.deep.equal({ traceparent });
        });

        it('should keep the first 32 tracestate members that follow the grammar, long ones included', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const long = (i: number) => `long${i}=${'v'.repeat(130)}`;
            const valid = Array.from({ length: 34 }, (_, i) => (i === 3 || i === 5 ? long(i) : `k${i}=v`));

            const request = await withTraceContext({ traceparent, tracestate: ['Bad=1', ...valid].join(',') }, () => sut.call());
            const cutFromTheEnd = await withTraceContext({ traceparent, tracestate: members(40) }, () => sut.call());

            expect(request.Headers!.tracestate).to.equal(valid.slice(0, 32).join(','));
            expect(cutFromTheEnd.Headers!.tracestate).to.equal(members(32));
        });

        it('should send the first 64 baggage members', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ traceparent, baggage: members(65) }, () => sut.call());

            expect(request.Headers!.baggage).to.equal(members(64));
        });

        it('should drop a baggage member that would go over 8192 UTF-8 bytes', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const fits = `k=${'v'.repeat(8190)}`;
            const fitting = await withTraceContext({ traceparent, baggage: fits }, () => sut.call());
            const tooBig = await withTraceContext({ traceparent, baggage: `a=1,k=${'é'.repeat(4095)}` }, () => sut.call());

            expect(fitting.Headers!.baggage).to.equal(fits);
            expect(tooBig.Headers!.baggage).to.equal('a=1');
        });

        it('should not send baggage members a receiver cannot parse', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ traceparent, baggage: 'foo,a=1,,=x' }, () => sut.call());
            const nothingLeft = await withTraceContext({ traceparent, baggage: 'foo' }, () => sut.call());

            expect(request.Headers!.baggage).to.equal('a=1');
            expect(nothingLeft.Headers).to.deep.equal({ traceparent });
        });

        it('should send the baggage alone, without the tracestate, when there is no traceparent', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ tracestate: 'a=b', baggage: 'k=v' }, () => sut.call());

            expect(request.Headers).to.deep.equal({ baggage: 'k=v' });
        });

        it('should send the ambient trace context without an override', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withAmbientTraceContext(() => sut.call());

            expect(request.Headers).to.deep.equal(ambientTraceContext);
        });

        it('should send the ambient traceparent and tracestate with the baggage of an override that has no traceparent', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withAmbientTraceContext(() => withTraceContext({ tracestate: 'ignored=1', baggage: 'userId=alice' }, () => sut.call()));

            expect(request.Headers).to.deep.equal({ traceparent: otherTraceparent, tracestate: 'congo=t61rcWkgMzE', baggage: 'userId=alice' });
        });

        it('should let an override with a traceparent win outright over the ambient trace context', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withAmbientTraceContext(() => withTraceContext({ traceparent, baggage: 'userId=alice' }, () => sut.call()));

            expect(request.Headers).to.deep.equal({ traceparent, baggage: 'userId=alice' });
        });

        it('should drop tracestate members that break the grammar', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ traceparent, tracestate: 'congo=t61rcWkgMzE,Bad=1' }, () => sut.call());

            expect(request.Headers!.tracestate).to.equal('congo=t61rcWkgMzE');
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

        it('should look header names up case-insensitively', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            let seen: TraceContext | undefined;
            await sut.receiveCallback({ TraceParent: traceparent, TRACESTATE: 'a=b' }, () => {
                seen = currentIncomingTraceContext();
            });

            expect(seen).to.deep.equal({ traceparent, tracestate: 'a=b' });
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

        it('should accept a higher-version traceparent as it is', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const future = '01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-future';
            let seen: TraceContext | undefined;
            await sut.receiveCallback({ traceparent: future }, () => {
                seen = currentIncomingTraceContext();
            });

            expect(seen).to.deep.equal({ traceparent: future });
        });

        it('should drop received tracestate members that break the grammar', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            let seen: TraceContext | undefined;
            await sut.receiveCallback({ traceparent, tracestate: '2020-01-01T00:00:00Z, congo=t61rcWkgMzE ,Bad=1,,x=a=b' }, () => {
                seen = currentIncomingTraceContext();
            });

            expect(seen).to.deep.equal({ traceparent, tracestate: 'congo=t61rcWkgMzE' });
        });

        it('should receive a baggage that arrives without a traceparent', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            let seen: TraceContext | undefined;
            await sut.receiveCallback({ baggage: 'k=v' }, () => {
                seen = currentIncomingTraceContext();
            });

            expect(seen).to.deep.equal({ baggage: 'k=v' });
        });

        for (const [name, headers] of [
            ['33 tracestate members', { traceparent, tracestate: members(33) }],
            ['65 baggage members', { traceparent, baggage: members(65) }],
            ['a baggage over 8192 bytes', { traceparent, baggage: `k=${'v'.repeat(8191)}` }],
        ] as Array<[string, TraceContext]>) {
            it(`should accept ${name} as is`, async () => {
                const sut = createSut(TelemetryPropagationPolicy.Both);
                let seen: TraceContext | undefined;
                const response = await sut.receiveCallback(headers, () => {
                    seen = currentIncomingTraceContext();
                });

                expect(response.Error).to.equal(null);
                expect(seen).to.deep.equal(headers);
            });
        }

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
        it('should percent-encode like .NET Uri.EscapeDataString', () => {
            expect(
                TraceHeaders.encodeBaggage([
                    ['userId', 'alice'],
                    ['x', "a b,c=d;!'()*é"],
                ]),
            ).to.equal('userId=alice,x=a%20b%2Cc%3Dd%3B%21%27%28%29%2A%C3%A9');
            expect(TraceHeaders.encodeBaggage([])).to.equal(undefined);
        });

        it('should send a lone surrogate as U+FFFD, as .NET and Python do', () => {
            expect(
                TraceHeaders.encodeBaggage([
                    ['k\uDC00', 'a\uD800b'],
                    ['pair', '\uD83D\uDE00'],
                    ['reversed', '\uDE00\uD83D'],
                ]),
            ).to.equal('k%EF%BF%BD=a%EF%BF%BDb,pair=%F0%9F%98%80,reversed=%EF%BF%BD%EF%BF%BD');
        });

        it('should decode an encoded , = and ; back exactly', () => {
            const entries: Array<[string, string]> = [
                ['userId', 'alice'],
                ['x', 'a b,c=d;e'],
            ];
            expect(TraceHeaders.decodeBaggage(TraceHeaders.encodeBaggage(entries)!)).to.deep.equal(entries);
        });

        it('should skip members with no key, trim keys and values, and ignore properties', () => {
            expect(TraceHeaders.decodeBaggage('foo,a=1,,=x, b = 2 ,c=3;ttl=30,d;p=1,')).to.deep.equal([
                ['a', '1'],
                ['b', '2'],
                ['c', '3'],
            ]);
        });

        for (const [escaped, decoded] of [
            ['50%', '50%'],
            ['%zz%41', '%zzA'],
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
            it(`should decode ${escaped} like .NET and Python, with U+FFFD for what isn't UTF-8`, () => {
                expect(TraceHeaders.decodeBaggage(`k=${escaped}`)).to.deep.equal([['k', decoded]]);
            });
        }
    });
});
