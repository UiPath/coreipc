import { Observer } from 'rxjs';
import { expect } from 'chai';

import {
    Address,
    ArgumentError,
    CallbackStoreImpl,
    CancellationToken,
    ChannelManager,
    ConfigStore,
    ConnectHelper,
    ContractStore,
    IMessageStream,
    IRpcChannel,
    IRpcChannelFactory,
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

function createSut(policy: TelemetryPropagationPolicy) {
    const contractStore = new ContractStore();
    const callbackStore = new CallbackStoreImpl();
    const configStore = new ConfigStore<any>(new MockServiceProvider<any>({ implementation: { contractStore } }));
    configStore.telemetryPropagation = policy;
    const sp = new MockServiceProvider<any>({ implementation: { configStore, contractStore, callbackStore } });
    const factory = new CapturingChannelFactory();
    const address = new MockAddress();
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

        it('should reject more than 32 tracestate members', () => {
            expect(run({ traceparent, tracestate: members(32) })).to.equal(1);
            expectArgumentError(() => run({ traceparent, tracestate: members(33) }), 'tracestate');
        });

        it('should reject more than 64 baggage members or 8192 UTF-8 bytes', () => {
            expect(run({ traceparent, baggage: members(64) })).to.equal(1);
            expectArgumentError(() => run({ traceparent, baggage: members(65) }), 'baggage');

            expect(run({ traceparent, baggage: `k=${'é'.repeat(4095)}` })).to.equal(1);
            expectArgumentError(() => run({ traceparent, baggage: `k=${'é'.repeat(4096)}` }), 'baggage');
        });

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

        it('should omit empty tracestate and baggage', async () => {
            const sut = createSut(TelemetryPropagationPolicy.Both);
            const request = await withTraceContext({ traceparent, tracestate: '', baggage: '' }, () => sut.call());

            expect(request.Headers).to.deep.equal({ traceparent });
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

        it('should decode an encoded , = and ; back exactly', () => {
            const entries: Array<[string, string]> = [
                ['userId', 'alice'],
                ['x', 'a b,c=d;e'],
            ];
            expect(TraceHeaders.decodeBaggage(TraceHeaders.encodeBaggage(entries)!)).to.deep.equal(entries);
        });

        it('should leave an escape that is not valid UTF-8 as it is, like .NET Uri.UnescapeDataString', () => {
            expect(TraceHeaders.decodeBaggage('k=50%,x=%zz%41,y=%FF%C3%A9')).to.deep.equal([
                ['k', '50%'],
                ['x', '%zzA'],
                ['y', '%FFé'],
            ]);
        });
    });
});
