// tslint:disable: max-line-length
// tslint:disable: no-unused-expression

import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';
import chaiAsPromised from 'chai-as-promised';
use(spies);
use(chaiAsPromised);

import { ProxyFactory, symbolofBroker, Generator } from '../../../src/core/internals/proxy-factory';
import { IBroker } from '../../../src/core/internals/broker';
import { RemoteError, __returns__, __hasCancellationToken__ } from '../../../src/core/surface';

import * as BrokerMessage from '../../../src/core/internals/broker-message';

import { CancellationTokenSource, CancellationToken, TimeSpan } from '../../../src/foundation/threading';
import { ArgumentNullError, OperationCanceledError, AggregateError } from '../../../src/foundation/errors';

describe(`core:internals -> class:ProxyFactory`, () => {
    context(`method:create`, () => {
        it(`should throw ArgumentNullError provided a falsy sampleCtor`, () => {
            (() => ProxyFactory.create(null as any, {} as any)).
                should.throw(ArgumentNullError).
                with.property('paramName', 'sampleCtor');
        });

        it(`should throw ArgumentNullError provided a truthy sampleCtor and a falsy broker`, () => {
            (() => ProxyFactory.create(Object, null as any)).
                should.throw(ArgumentNullError).
                with.property('paramName', 'broker');
        });

        it(`shouldn't throw provided truthy args`, () => {
            (() => ProxyFactory.create(Object, {} as any)).
                should.not.throw(ArgumentNullError);
        });

        it(`should return a truthy reference`, () => {
            const obj = ProxyFactory.create(Object, {} as any);
            expect(obj).not.to.be.null;
            expect(obj).not.to.be.undefined;
        });

        it(`should return a different instance every time it's called`, () => {
            const obj1 = ProxyFactory.create(Object, {} as any);
            const obj2 = ProxyFactory.create(Object, {} as any);

            obj1.should.not.be.equal(obj2);
        });

        it(`should return instances with the same prototype provided the same sampleCtor every time it's called`, () => {
            const obj1 = ProxyFactory.create(Object, {} as any) as any;
            const obj2 = ProxyFactory.create(Object, {} as any) as any;

            obj1.__proto__.should.equal(obj2.__proto__);
        });

        it(`should return an instances with different prototypes provided different sampleCtor values`, () => {
            class Class1 { }
            class Class2 { }
            const obj1 = ProxyFactory.create(Class1, {} as any) as any;
            const obj2 = ProxyFactory.create(Class2, {} as any) as any;

            obj1.__proto__.should.not.equal(obj2.__proto__);
        });

        it(`should return an instance whose all keys are inherited from its prototype except for "symbolofBroker"`, () => {
            class Source {
                public method1(): void { throw null; }
                public method2(): number { throw null; }
                public method3(): string { throw null; }
            }
            const obj = ProxyFactory.create(Source, {} as any);
            const ownKeys = Reflect.ownKeys(obj);

            ownKeys.length.should.be.equal(1);
            ownKeys[0].should.be.equal(symbolofBroker);
        });
    });
});

describe(`core:internals -> class:Generator`, () => {
    context(`method:Generate`, () => {
        it(`should throw ArgumentNullError provided a falsy sampleCtor`, () => {
            (() => Generator.generate(null as any)).
                should.throw(ArgumentNullError).
                with.property('paramName', 'sampleCtor');
        });

        it(`shouldn't throw provided a truthy sampleCtor`, () => {
            (() => Generator.generate(Object)).should.not.throw();
        });

        context(`the returned value`, () => {
            it(`should be truthy`, () => {
                const obj = Generator.generate(Object);
                expect(obj).not.to.null;
                expect(obj).not.to.undefined;
            });

            it(`should be a Function`, () => {
                const obj = Generator.generate(Object);
                obj.should.be.instanceOf(Function);
            });

            it(`should have a prototype which matches the sampleCtor's own keys which refer to methods`, () => {
                class IContract {
                    public member1(x: number): Promise<void> { throw null; }
                    public member2(): Promise<void> { throw null; }
                    public member3: number = 123;
                    public get member4(): string { throw null; }
                }
                const obj = Generator.generate(IContract);
                const keys = Reflect.ownKeys(obj.prototype);
                keys.should.be.eql(['member1', 'member2']);
            });

            it(`should be a ctor which doesn't throw when used`, () => {
                const ctor = Generator.generate(Object);
                (() => new ctor({} as any)).should.not.throw();
            });

            context(`the proxy instantiated using the ctor`, () => {
                function quickBroker(sendReceiveAsync: (brokerRequest: BrokerMessage.Request) => Promise<BrokerMessage.Response>): IBroker {
                    const broker: IBroker = {
                        async disposeAsync(): Promise<void> { },
                        sendReceiveAsync: spy(sendReceiveAsync)
                    };
                    return broker;
                }
                class TestError extends Error {
                    constructor(message: string) { super(message); }
                }

                it(`should dispatch its calls to the IBroker instance`, async () => {
                    class IContract {
                        public testMethod(x: string): Promise<string> { throw null; }
                    }
                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        brokerRequest.args.should.be.eql(['test-argument']);
                        brokerRequest.methodName.should.be.equal('testMethod');

                        return new BrokerMessage.Response('test-result', null);
                    });

                    const proxy = new ctor(broker);

                    const promise = proxy.testMethod('test-argument');
                    await promise.should.eventually.be.fulfilled.and.equal('test-result');
                    broker.sendReceiveAsync.should.have.been.called();
                });

                it(`should throw RemoteError for method calls provided the IBroker instance gracefully returns an error`, async () => {
                    class IContract {
                        public testMethod(): Promise<string> { throw null; }
                    }
                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        return new BrokerMessage.Response(null, new TestError('test-message'));
                    });

                    const proxy = new ctor(broker);
                    const promise = proxy.testMethod();

                    await promise.should.eventually.be.rejectedWith(RemoteError).
                        which.satisfies((x: RemoteError) => {
                            x.receivedError.should.be.instanceOf(TestError);
                            x.receivedError.message.should.be.equal('test-message');
                            return true;
                        });
                });

                it(`should throw OperationCanceledError for method calls, when the CT is signalled`, async () => {
                    class IContract {
                        public testMethod(ct: CancellationToken): Promise<string> { throw null; }
                    }
                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        const ct = brokerRequest.args[0] as CancellationToken;
                        await Promise.delay(100, ct);

                        return new BrokerMessage.Response('test-result', null);
                    });

                    const proxy = new ctor(broker);
                    const cts = new CancellationTokenSource(TimeSpan.fromMilliseconds(0));
                    const promise = proxy.testMethod(cts.token);

                    await promise.should.eventually.be.rejectedWith(OperationCanceledError);
                });

                it(`should throw AggregateError for method calls, when an error occurs locally`, async () => {
                    class IContract {
                        public testMethod(): Promise<string> { throw null; }
                    }
                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        throw new TestError('test-message');
                    });

                    const proxy = new ctor(broker);
                    const promise = proxy.testMethod();

                    await promise.should.eventually.be.rejectedWith(AggregateError).
                        which.satisfies((x: AggregateError) => {
                            x.errors.length.should.be.equal(1);
                            x.errors[0].should.be.instanceOf(TestError).
                                with.property('message', 'test-message');
                            return true;
                        });
                });

                it(`should return a class instance provided the contract's method is decorated with @__returns__`, async () => {
                    class Complex {
                        constructor(public readonly real: number, public readonly imaginary: number) { }
                    }
                    class IContract {
                        @__returns__(Complex)
                        public createComplex(real: number, imaginary: number): Promise<Complex> { throw null; }
                    }

                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        const real = brokerRequest.args[0] as number;
                        const imaginary = brokerRequest.args[1] as number;
                        return new BrokerMessage.Response({ real, imaginary }, null);
                    });

                    const proxy = new ctor(broker);
                    await proxy.createComplex(10, 20).
                        should.eventually.be.instanceOf(Complex);
                });

                it(`shouldn't throw for a null return provided the contract's method is decorated with @__returns__`, async () => {
                    class Complex {
                        constructor(public readonly real: number, public readonly imaginary: number) { }
                    }
                    class IContract {
                        @__returns__(Complex)
                        public createComplex(real: number, imaginary: number): Promise<Complex> { throw null; }
                    }

                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        // const real = brokerRequest.args[0] as number;
                        // const imaginary = brokerRequest.args[1] as number;
                        // const result = new Complex(real, imaginary);
                        return new BrokerMessage.Response(null, null);
                    });

                    const proxy = new ctor(broker);
                    await proxy.createComplex(10, 20).
                        should.eventually.be.null;
                });

                it(`should normalize it's args and trim a potential trailing undefined, when the contract's method isn't decorated with @__hasCancellationToken__`, async () => {
                    class IContract {
                        public testMethod(a: number, b: string, ct?: CancellationToken): Promise<void> { throw null; }
                    }

                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        brokerRequest.args.should.be.eql([123, 'foo']);
                        return new BrokerMessage.Response(null, null);
                    });

                    const proxy = new ctor(broker);
                    await proxy.testMethod(123, 'foo', undefined as any);

                    broker.sendReceiveAsync.should.have.been.called();
                });

                it(`should normalize it's args and not mind a missing last arg, when the contract's method isn't decorated with @__hasCancellationToken__`, async () => {
                    class IContract {
                        public testMethod(a: number, b: string, ct?: CancellationToken): Promise<void> { throw null; }
                    }

                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        brokerRequest.args.should.be.eql([123, 'foo']);
                        return new BrokerMessage.Response(null, null);
                    });

                    const proxy = new ctor(broker);
                    await proxy.testMethod(123, 'foo');

                    broker.sendReceiveAsync.should.have.been.called();
                });

                it(`should normalize it's args and trim a potential trailing undefined while adding CancellationToken.none, when the contract's method is decorated with @__hasCancellationToken__`, async () => {
                    class IContract {
                        @__hasCancellationToken__
                        public testMethod(a: number, b: string, ct?: CancellationToken): Promise<void> { throw null; }
                    }

                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        brokerRequest.args.should.be.eql([123, 'foo', CancellationToken.none]);
                        return new BrokerMessage.Response(null, null);
                    });

                    const proxy = new ctor(broker);
                    await proxy.testMethod(123, 'foo', undefined as any);

                    broker.sendReceiveAsync.should.have.been.called();
                });

                it(`should normalize it's args and not mind a missing last arg, when the contract's method is decorated with @__hasCancellationToken__`, async () => {
                    class IContract {
                        @__hasCancellationToken__
                        public testMethod(a: number, b: string, ct?: CancellationToken): Promise<void> { throw null; }
                    }

                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        brokerRequest.args.should.be.eql([123, 'foo', CancellationToken.none]);
                        return new BrokerMessage.Response(null, null);
                    });

                    const proxy = new ctor(broker);
                    await proxy.testMethod(123, 'foo');

                    broker.sendReceiveAsync.should.have.been.called();
                });

                it(`should normalize it's args and not mind a present ct as its last arg, when the contract's method is decorated with @__hasCancellationToken__`, async () => {
                    class IContract {
                        @__hasCancellationToken__
                        public testMethod(a: number, b: string, ct?: CancellationToken): Promise<void> { throw null; }
                    }

                    const ctor = Generator.generate(IContract);
                    const broker = quickBroker(async (brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> => {
                        brokerRequest.args.should.be.eql([123, 'foo', CancellationToken.none]);
                        return new BrokerMessage.Response(null, null);
                    });

                    const proxy = new ctor(broker);
                    await proxy.testMethod(123, 'foo', CancellationToken.none);

                    broker.sendReceiveAsync.should.have.been.called();
                });
            });
        });
    });
});
