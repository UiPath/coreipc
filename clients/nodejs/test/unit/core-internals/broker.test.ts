// tslint:disable: max-line-length
// tslint:disable: no-unused-expression

import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';
import chaiAsPromised from 'chai-as-promised';

use(spies);
use(chaiAsPromised);

import * as BrokerMessage from '../../../src/core/internals/broker-message';
import * as WireMessage from '../../../src/core/internals/wire-message';

import { Broker, IBrokerCtorParams } from '../../../src/core/internals/broker';
import { ConnectionFactoryDelegate } from '../../../src/core/surface';
import { MethodContainer } from '../../../src/foundation/utils';

import { SerializationPal } from '../../../src/core/internals/serialization-pal';
import { TimeSpan } from '../../../src/foundation/threading';
import { ArgumentNullError, ArgumentError } from '../../../src/foundation/errors';

import { PipeClientStream, IPipeClientStream } from '../../../src/foundation/pipes';
import { ILogicalSocket } from '../../../src/foundation/pipes/logical-socket';
import { CancellationToken, PromiseCompletionSource } from '../../../src/foundation/threading';
import { Subject, ReplaySubject, Observable } from 'rxjs';

describe(`core:internals -> class:Broker`, () => {
    class EmptyLogicalSocket implements ILogicalSocket {
        public readonly data = new Observable<Buffer>();
        public async connectAsync(path: string, maybeTimeout: TimeSpan, cancellationToken: CancellationToken): Promise<void> {
        }
        public async writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
        }
        public dispose(): void {
        }
    }

    class ShortCircuitLogicalSocket implements ILogicalSocket {
        public readonly _data = new Subject<Buffer>();
        public get data(): Observable<Buffer> { return this._data; }

        constructor(private readonly _target: MethodContainer) { }

        public async connectAsync(path: string, maybeTimeout: TimeSpan, cancellationToken: CancellationToken): Promise<void> { }

        public async writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
            (async () => {
                buffer.readUInt8(0).should.be.equal(WireMessage.Type.Request as number);
                buffer.readInt32LE(1).should.be.equal(buffer.length - 5);
                const wireRequest = SerializationPal.fromJson(buffer.subarray(5).toString('utf-8'), WireMessage.Type.Request);
                const obj = SerializationPal.deserializeRequest(wireRequest);

                let brokerResponse: BrokerMessage.Response = null as any;
                try {
                    const method = this._target[obj.brokerRequest.methodName];
                    if (method == null) {
                        throw new Error(`Method "${method}" not found`);
                    }
                    let result = method.apply(this._target as any, obj.brokerRequest.args as any);
                    if (result instanceof Promise) {
                        result = await result;
                    }
                    brokerResponse = new BrokerMessage.Response(result, null);
                } catch (error) {
                    brokerResponse = new BrokerMessage.Response(null, error);
                }

                const responseBuffer = SerializationPal.wireResponseToBuffer(
                    SerializationPal.brokerResponseToWireResponse(brokerResponse, obj.id)
                );
                this._data.next(responseBuffer);
            })();
        }

        public dispose(): void { }
    }

    function createBroker(remoteMethodContainer?: MethodContainer) {
        return createBrokerFromLogicalSocket(
            remoteMethodContainer ? new ShortCircuitLogicalSocket(remoteMethodContainer) : new EmptyLogicalSocket()
        );
    }
    function createBrokerFromLogicalSocket(logicalSocket: ILogicalSocket, callbackContainer?: MethodContainer, connectionFactory?: ConnectionFactoryDelegate): Broker {
        const params: IBrokerCtorParams = {
            factory: () => logicalSocket,
            pipeName: 'pipe-name',
            connectTimeout: TimeSpan.fromSeconds(1000),
            defaultCallTimeout: TimeSpan.fromSeconds(1000),
            callback: callbackContainer,
            connectionFactory: connectionFactory || null,
            beforeCall: null,
            traceNetwork: false
        };
        return new Broker(params);
    }
    function createBrokerWithConnectionFactory(connectionFactory: ConnectionFactoryDelegate, methodContainer?: MethodContainer) {
        return createBrokerFromLogicalSocket(
            methodContainer ? new ShortCircuitLogicalSocket(methodContainer) : new EmptyLogicalSocket(),
            undefined,
            connectionFactory
        );
    }
    context(`ctor`, () => {
        it(`should throw provided a falsy _parameters arg`, () => {
            (() => new Broker(null as any)).should.throw(ArgumentNullError).with.property('paramName', '_parameters');
        });

        it(`should throw provided a falsy factory`, () => {
            const params = {
                factory: null as any,
                pipeName: 'pipe-name',
                connectTimeout: TimeSpan.fromSeconds(1),
                defaultCallTimeout: TimeSpan.fromSeconds(1)
            };

            (() => new Broker(params)).should.throw(ArgumentError).with.property('paramName', '_parameters');
        });

        it(`should throw provided a falsy pipeName`, () => {
            const params = {
                factory: () => ({} as any),
                pipeName: null as any,
                connectTimeout: TimeSpan.fromSeconds(1),
                defaultCallTimeout: TimeSpan.fromSeconds(1)
            };

            (() => new Broker(params)).should.throw(ArgumentError).with.property('paramName', '_parameters');
        });

        it(`should throw provided a falsy connectTimeout`, () => {
            const params = {
                factory: () => ({} as any),
                pipeName: 'pipe-name',
                connectTimeout: null as any,
                defaultCallTimeout: TimeSpan.fromSeconds(1)
            };

            (() => new Broker(params)).should.throw(ArgumentError).with.property('paramName', '_parameters');
        });

        it(`should throw provided a falsy defaultCallTimeout`, () => {
            const params = {
                factory: () => ({} as any),
                pipeName: 'pipe-name',
                connectTimeout: TimeSpan.fromSeconds(1),
                defaultCallTimeout: null as any
            };

            (() => new Broker(params)).should.throw(ArgumentError).with.property('paramName', '_parameters');
        });

        it(`shouldn't throw provided truthy 1st 4 args`, () => {
            const params = {
                factory: () => ({} as any),
                pipeName: 'pipe-name',
                connectTimeout: TimeSpan.fromSeconds(1),
                defaultCallTimeout: TimeSpan.fromSeconds(1)
            };

            (() => new Broker(params)).should.not.throw();
        });
    });

    describe(`feature:connection factory`, () => {
        it(`the provided ConnectionFactory should get called before connecting`, async () => {
            const spyConnectionFactory = spy(async () => { });
            const broker = createBrokerWithConnectionFactory(
                spyConnectionFactory, {
                    async sumAsync(x: number, y: number) {
                        return x + y;
                    }
                }
            );

            await broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('sumAsync', [1, 2]));
            spyConnectionFactory.should.have.been.called();
        });

        it(`the provided ConnectionFactory should get called before connecting and not returning a value should signify connecting off the shelf`, async () => {
            const spyConnectionFactory = spy(async () => { });
            const broker = createBrokerWithConnectionFactory(
                spyConnectionFactory, {
                    async sumAsync(x: number, y: number) {
                        return x + y;
                    }
                }
            );

            await broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('sumAsync', [1, 2]));
            spyConnectionFactory.should.have.been.called();
        });

        it(`the provided ConnectionFactory should get called before connecting and returning a PipeClientStream should prime the Broker with that PipeClientStream`, async () => {
            const methodContainer: MethodContainer = {
                async sumAsync(x: number, y: number) {
                    return x + y;
                }
            };
            const spyConnectionFactory = spy(async (connect: () => Promise<IPipeClientStream>, cancellationToken: CancellationToken): Promise<PipeClientStream | void> => {
                return await PipeClientStream.connectAsync(
                    () => new ShortCircuitLogicalSocket(methodContainer),
                    'pipe-name',
                    null,
                    false,
                    cancellationToken);
            });
            const broker = createBrokerWithConnectionFactory(
                spyConnectionFactory,
                methodContainer
            );

            await broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('sumAsync', [1, 2]));
            spyConnectionFactory.should.have.been.called();
        });
    });

    describe(`feature:callbacks`, () => {
        it(`a broker with no callback method container should respond to requests with InvalidOperationError`, async () => {
            const data = new ReplaySubject<Buffer>();

            const ls: ILogicalSocket = {
                data,
                async connectAsync() { },
                dispose() { },
                writeAsync: null as any
            };

            const write2 = spy(async (responseBuffer: Buffer, ct: CancellationToken): Promise<void> => {
                expect(responseBuffer).not.to.be.null.and.not.to.be.undefined;

                expect(responseBuffer.length).to.be.greaterThan(0);
                expect(responseBuffer.readInt8(0)).to.equal(WireMessage.Type.Response);
                expect(responseBuffer.readInt32LE(1)).to.equal(responseBuffer.length - 5);
                const wresp = SerializationPal.fromJson(responseBuffer.subarray(5).toString('utf-8'), WireMessage.Type.Response);
                const brespTuple = SerializationPal.deserializeResponse(wresp);
                expect(brespTuple.brokerResponse.maybeResult).to.be.null;
                expect(brespTuple.brokerResponse.maybeError).to.be.instanceOf(Error);
                expect((brespTuple.brokerResponse.maybeError as any).name).to.equal('InvalidOperationError');
                expect((brespTuple.brokerResponse.maybeError as any).message).to.equal('Callbacks are not supported by this IpcClient.');
            });
            const write1 = spy(async (requestBuffer: Buffer, ct: CancellationToken): Promise<void> => {
                ls.writeAsync = write2;

                expect(requestBuffer).not.to.be.null.and.not.to.be.undefined;
                expect(requestBuffer.length).to.be.greaterThan(0);
                expect(requestBuffer.readInt8(0)).to.equal(WireMessage.Type.Request);
                expect(requestBuffer.readInt32LE(1)).to.equal(requestBuffer.length - 5);
                const wreq = SerializationPal.fromJson(requestBuffer.subarray(5).toString('utf-8'), WireMessage.Type.Request);
                const breqTuple = SerializationPal.deserializeRequest(wreq);
                expect(breqTuple.brokerRequest.methodName).to.equal('primerMethod');
                expect(breqTuple.brokerRequest.args).to.deep.equal([]);

                const bresp = new BrokerMessage.Response(null, null);
                const responseBuffer = SerializationPal.wireResponseToBuffer(SerializationPal.brokerResponseToWireResponse(bresp, breqTuple.id));

                data.next(responseBuffer);
            });
            ls.writeAsync = write1;

            const broker = createBrokerFromLogicalSocket(ls);

            await broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('primerMethod', []));
            write1.should.have.been.called();
            write2.should.not.have.been.called();

            data.next(SerializationPal.wireRequestToBuffer(new WireMessage.Request(2, 'id', 'callbackMethodName', [])));

            write2.should.not.have.been.called();

            await Promise.yield();
            write2.should.have.been.called();
        });

        it(`a broker with a callback method container should respond to requests to inexistent methods with InvalidOperationError`, async () => {
            const data = new ReplaySubject<Buffer>();

            const ls: ILogicalSocket = {
                data,
                async connectAsync() { },
                dispose() { },
                writeAsync: null as any
            };

            const write2 = spy(async (responseBuffer: Buffer, ct: CancellationToken): Promise<void> => {
                expect(responseBuffer).not.to.be.null.and.not.to.be.undefined;

                expect(responseBuffer.length).to.be.greaterThan(0);
                expect(responseBuffer.readInt8(0)).to.equal(WireMessage.Type.Response);
                expect(responseBuffer.readInt32LE(1)).to.equal(responseBuffer.length - 5);
                const wresp = SerializationPal.fromJson(responseBuffer.subarray(5).toString('utf-8'), WireMessage.Type.Response);
                const brespTuple = SerializationPal.deserializeResponse(wresp);
                expect(brespTuple.brokerResponse.maybeResult).to.be.null;
                expect(brespTuple.brokerResponse.maybeError).to.be.instanceOf(Error);
                expect((brespTuple.brokerResponse.maybeError as any).name).to.equal('InvalidOperationError');
                expect((brespTuple.brokerResponse.maybeError as any).message).to.equal(`Method not found.\r\nMethod name: callbackMethodName`);
            });
            const write1 = spy(async (requestBuffer: Buffer, ct: CancellationToken): Promise<void> => {
                ls.writeAsync = write2;

                expect(requestBuffer).not.to.be.null.and.not.to.be.undefined;
                expect(requestBuffer.length).to.be.greaterThan(0);
                expect(requestBuffer.readInt8(0)).to.equal(WireMessage.Type.Request);
                expect(requestBuffer.readInt32LE(1)).to.equal(requestBuffer.length - 5);
                const wreq = SerializationPal.fromJson(requestBuffer.subarray(5).toString('utf-8'), WireMessage.Type.Request);
                const breqTuple = SerializationPal.deserializeRequest(wreq);
                expect(breqTuple.brokerRequest.methodName).to.equal('primerMethod');
                expect(breqTuple.brokerRequest.args).to.deep.equal([]);

                const bresp = new BrokerMessage.Response(null, null);
                const responseBuffer = SerializationPal.wireResponseToBuffer(SerializationPal.brokerResponseToWireResponse(bresp, breqTuple.id));

                data.next(responseBuffer);
            });
            ls.writeAsync = write1;

            const callbackContainer = {};
            const broker = createBrokerFromLogicalSocket(ls, callbackContainer);

            await broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('primerMethod', []));
            write1.should.have.been.called();
            write2.should.not.have.been.called();

            data.next(SerializationPal.wireRequestToBuffer(new WireMessage.Request(2, 'id', 'callbackMethodName', [])));

            write2.should.not.have.been.called();

            await Promise.yield();
            write2.should.have.been.called();
        });

        it(`responding from a callback with a successful result works`, async () => {
            const data = new ReplaySubject<Buffer>();

            const ls: ILogicalSocket = {
                data,
                async connectAsync() { },
                dispose() { },
                writeAsync: null as any
            };

            const write2 = spy(async (responseBuffer: Buffer, ct: CancellationToken): Promise<void> => {
                expect(responseBuffer).not.to.be.null.and.not.to.be.undefined;

                expect(responseBuffer.length).to.be.greaterThan(0);
                expect(responseBuffer.readInt8(0)).to.equal(WireMessage.Type.Response);
                expect(responseBuffer.readInt32LE(1)).to.equal(responseBuffer.length - 5);
                const wresp = SerializationPal.fromJson(responseBuffer.subarray(5).toString('utf-8'), WireMessage.Type.Response);
                const brespTuple = SerializationPal.deserializeResponse(wresp);
                expect(brespTuple.brokerResponse.maybeResult).to.be.null;
                expect(brespTuple.brokerResponse.maybeError).to.be.instanceOf(Error);
                expect((brespTuple.brokerResponse.maybeError as any).name).to.equal('InvalidOperationError');
                expect((brespTuple.brokerResponse.maybeError as any).message).to.equal(`Method not found.\r\nMethod name: callbackMethodName`);
            });
            const write1 = spy(async (requestBuffer: Buffer, ct: CancellationToken): Promise<void> => {
                ls.writeAsync = write2;

                expect(requestBuffer).not.to.be.null.and.not.to.be.undefined;
                expect(requestBuffer.length).to.be.greaterThan(0);
                expect(requestBuffer.readInt8(0)).to.equal(WireMessage.Type.Request);
                expect(requestBuffer.readInt32LE(1)).to.equal(requestBuffer.length - 5);
                const wreq = SerializationPal.fromJson(requestBuffer.subarray(5).toString('utf-8'), WireMessage.Type.Request);
                const breqTuple = SerializationPal.deserializeRequest(wreq);
                expect(breqTuple.brokerRequest.methodName).to.equal('primerMethod');
                expect(breqTuple.brokerRequest.args).to.deep.equal([]);

                const bresp = new BrokerMessage.Response(null, null);
                const responseBuffer = SerializationPal.wireResponseToBuffer(SerializationPal.brokerResponseToWireResponse(bresp, breqTuple.id));

                data.next(responseBuffer);
            });
            ls.writeAsync = write1;

            const callbackContainer = {
                async callbackMethod(x: number): Promise<number> {
                    return x * x;
                }
            };
            const broker = createBrokerFromLogicalSocket(ls, callbackContainer);

            await broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('primerMethod', []));
            data.next(SerializationPal.wireRequestToBuffer(new WireMessage.Request(2, 'id', 'callbackMethod', ['3'])));

            await Promise.yield();
        });
    });

    context(`method:disposeAsync`, () => {
        it(`shouldn't throw and shouldn't reject provided the broker hadn't connected yet (even when called multiple times)`, async () => {
            const broker = createBroker();
            let promise: Promise<void> = null as any;

            (() => promise = broker.disposeAsync()).should.not.throw();
            await promise.should.eventually.not.be.rejected;

            (() => promise = broker.disposeAsync()).should.not.throw();
            await promise.should.eventually.not.be.rejected;
        });

        it(`shouldn't throw and shouldn't reject provided the broker had connected (even when called multiple times)`, async () => {
            const broker = createBroker({
                async sumAsync(x: number, y: number): Promise<number> {
                    await Promise.delay(1);
                    return x + y;
                }
            });
            const brokerRequest = new BrokerMessage.OutboundRequest('sumAsync', [1, 2]);
            await broker.sendReceiveAsync(brokerRequest).
                should.eventually.be.fulfilled.and.satisfy((x: BrokerMessage.Response) => {
                    expect(x).not.to.be.null.and.not.to.be.undefined;
                    expect(x.maybeError).to.be.null;
                    expect(x.maybeResult).to.be.equal(3);
                    return true;
                });

            let promise: Promise<void> = null as any;

            (() => promise = broker.disposeAsync()).should.not.throw();
            await promise.should.eventually.be.fulfilled;

            (() => promise = broker.disposeAsync()).should.not.throw();
            await promise.should.eventually.be.fulfilled;
        });

        it(`should cancel an in-flight outbound call`, async () => {
            const pcs = new PromiseCompletionSource<void>();
            const broker = createBroker({
                async sumAsync(x: number, y: number): Promise<number> {
                    await pcs.promise;
                    return x + y;
                }
            });
            const brokerRequest = new BrokerMessage.OutboundRequest('sumAsync', [1, 2]);
            const promise = broker.sendReceiveAsync(brokerRequest);

            const rejectedSpy = spy(() => { });
            promise.then(
                _ => { },
                rejectedSpy);

            await broker.disposeAsync();
            await Promise.yield();
            rejectedSpy.should.have.been.called();
            pcs.setResult(undefined);
        });

        it(`should await an in-flight callback`, async () => {
            const pcs = new PromiseCompletionSource<void>();
            const data = new ReplaySubject<Buffer>();
            const logicalSocket: ILogicalSocket = {
                data,
                async connectAsync(path: string, maybeTimeout: TimeSpan | null, ct: CancellationToken) {
                },
                async writeAsync(buffer: Buffer, ct: CancellationToken) {
                },
                dispose() { }
            };
            const succeedAsync = spy(async (x: number, y: number): Promise<number> => {
                await pcs.promise;
                return x + y;
            });
            const failAsync = spy(async (x: number, y: number): Promise<number> => {
                await pcs.promise;
                throw new Error();
            });
            const broker = createBrokerFromLogicalSocket(
                logicalSocket,
                {
                    succeedAsync,
                    failAsync
                });

            data.next(SerializationPal.wireRequestToBuffer(new WireMessage.Request(1, 'id-1', 'succeedAsync', ['1', '2'])));
            data.next(SerializationPal.wireRequestToBuffer(new WireMessage.Request(1, 'id-2', 'failAsync', ['1', '2'])));
            broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('inexistentMethod', [])).observe();

            await Promise.yield();
            succeedAsync.should.have.been.called();

            const promise = broker.disposeAsync();
            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            await Promise.yield();
            fulfilledSpy.should.not.have.been.called();

            pcs.setResult();
            await Promise.yield();

            fulfilledSpy.should.have.been.called();
        });
    });

    context(`method:sendReceiveAsync`, () => {
        it(`should reject provided a falsy brokerRequest`, async () => {
            const broker = createBroker();
            await broker.sendReceiveAsync(null as any).should.eventually.be.rejectedWith(ArgumentNullError).with.property('paramName', 'brokerRequest');
        });

        it(`should return a Promise`, async () => {
            const broker = createBroker();
            try {
                const invalidBrokerRequest: BrokerMessage.OutboundRequest = null as any;
                const validBrokerRequest = new BrokerMessage.OutboundRequest('method-name', []);

                function maybeObserve<T>(promise: Promise<T> | null | undefined): Promise<T> | null | undefined {
                    if (promise) {
                        promise.observe();
                    }
                    return promise;
                }

                expect(maybeObserve(broker.sendReceiveAsync(invalidBrokerRequest))).to.be.instanceOf(Promise);
                expect(maybeObserve(broker.sendReceiveAsync(validBrokerRequest))).to.be.instanceOf(Promise);
            } finally {
                try {
                    await broker.disposeAsync();
                } catch {
                }
            }
        });

        it(`should return a Promise which becomes fulfilled when the remote party responds`, async () => {
            const pcs = new PromiseCompletionSource<void>();
            const broker = createBroker({
                async succeedAsync(x: number, y: number) {
                    await pcs.promise;
                    return x + y;
                },
                async failLogicallyAsync() {
                    await pcs.promise;
                    throw new Error(`This should be wrapped in the BrokerMessage.Response.`);
                }
            });

            try {
                const breq1 = new BrokerMessage.OutboundRequest('succeedAsync', [1, 2]);
                const breq2 = new BrokerMessage.OutboundRequest('failLogicallyAsync', []);
                const breq3 = new BrokerMessage.OutboundRequest('breqFailInfrastructurallyAsync', []);

                const promise1 = broker.sendReceiveAsync(breq1);
                const promise2 = broker.sendReceiveAsync(breq2);
                const promise3 = broker.sendReceiveAsync(breq3);

                const fulfilled1Spy = spy((bresp: BrokerMessage.Response) => {
                    expect(bresp).to.be.instanceOf(BrokerMessage.Response);
                    expect(bresp.maybeError).to.be.null;
                    expect(bresp.maybeResult).to.equal(3);
                });
                const fulfilled2Spy = spy((bresp: BrokerMessage.Response) => {
                    expect(bresp).to.be.instanceOf(BrokerMessage.Response);
                    expect(bresp.maybeError).to.be.instanceOf(Error);
                    expect(bresp.maybeResult).to.be.null;
                });
                const fulfilled3Spy = spy((bresp: BrokerMessage.Response) => {
                    expect(bresp).to.be.instanceOf(BrokerMessage.Response);
                    expect(bresp.maybeError).to.be.instanceOf(Error);
                    expect(bresp.maybeResult).to.be.null;
                });

                promise1.then(fulfilled1Spy);
                promise2.then(fulfilled2Spy);
                promise3.then(fulfilled3Spy);

                await Promise.yield();
                fulfilled1Spy.should.not.have.been.called();
                fulfilled2Spy.should.not.have.been.called();

                fulfilled3Spy.should.have.been.called();

                pcs.setResult();
                await Promise.yield();
                fulfilled1Spy.should.have.been.called();
                fulfilled2Spy.should.have.been.called();

            } finally {
                try {
                    await broker.disposeAsync();
                } catch {
                }
            }
        });

        it(`should reject when connecting fails`, async () => {
            const data = new ReplaySubject<Buffer>();
            const ls: ILogicalSocket = {
                data,
                async connectAsync(path: string, maybeTimeout: TimeSpan | null, ct: CancellationToken) { throw new Error(); },
                async writeAsync(buffer: Buffer, ct: CancellationToken) { },
                dispose() { }
            };
            const broker = createBrokerFromLogicalSocket(ls);
            try {
                await broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('method', [])).
                    should.be.eventually.rejected;
            } finally {
                await broker.disposeAsync();
            }
        });
    });
});
