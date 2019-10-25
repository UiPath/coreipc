// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';

use(spies);

import * as Outcome from '@foundation/utils/outcome';
import * as BrokerMessage from '@core/internals/broker-message';

import { CallContext, CallbackContext, CallContextTable } from '@core/internals/context';
import { CancellationToken, CancellationTokenSource } from '@foundation/threading';
import { ArgumentNullError, OperationCanceledError, ObjectDisposedError } from '@foundation/errors';

describe(`core:internals -> class:CallContext`, () => {
    context(`ctor`, () => {
        it(`should throw provided a falsy id`, () => {
            (() => new CallContext(null as any, CancellationToken.none)).should.throw(ArgumentNullError).with.property('paramName', 'id');
            (() => new CallContext(undefined as any, CancellationToken.none)).should.throw(ArgumentNullError).with.property('paramName', 'id');
            (() => new CallContext('', CancellationToken.none)).should.throw(ArgumentNullError).with.property('paramName', 'id');
        });

        it(`should throw provided a falsy ct`, () => {
            (() => new CallContext('id', null as any)).should.throw(ArgumentNullError).with.property('paramName', 'cancellationToken');
            (() => new CallContext('id', undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'cancellationToken');
        });

        it(`should use the provided ct in the outcome of the promise property`, async () => {
            const cts = new CancellationTokenSource();
            const cc = new CallContext('id', cts.token);

            const rejectedSpy = spy((reason: any) => {
                expect(reason).to.be.instanceOf(OperationCanceledError);
            });
            cc.promise.then(_ => { }, rejectedSpy);

            await Promise.yield();
            rejectedSpy.should.not.have.been.called();

            cts.cancel();

            await Promise.yield();
            rejectedSpy.should.have.been.called();
        });
    });

    context(`property:promise`, () => {
        it(`shouldn't throw when accessed`, () => {
            const cc = new CallContext('id', CancellationToken.none);
            (() => cc.promise).should.not.throw;
        });

        it(`should return a Promise`, () => {
            const cc = new CallContext('id', CancellationToken.none);
            expect(cc.promise).to.be.instanceOf(Promise);
        });

        it(`should the same instance every time`, () => {
            const cc = new CallContext('id', CancellationToken.none);
            cc.promise.should.be.equal(cc.promise);
        });
    });

    context(`method:trySet`, () => {
        it(`should throw provided a falsy outcome`, () => {
            const cc = new CallContext('id', CancellationToken.none);
            (() => cc.trySet(null as any)).should.throw(ArgumentNullError).with.property('paramName', 'outcome');
            (() => cc.trySet(undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'outcome');
        });

        it(`shouldn't throw provided a truthy outcome even when called multiple times`, () => {
            const brokerResponse = new BrokerMessage.Response(123, null);
            const succeeded = new Outcome.Succeeded(brokerResponse);
            const faulted = new Outcome.Faulted<BrokerMessage.Response>(new Error());
            const canceled = new Outcome.Canceled<BrokerMessage.Response>();

            const cc = new CallContext('id', CancellationToken.none);
            (() => cc.trySet(succeeded)).should.not.throw;
            (() => cc.trySet(faulted)).should.not.throw;
            (() => cc.trySet(canceled)).should.not.throw;
        });

        it(`should cause the Promise to be fulfilled provided an Outcome.Succeeded`, async () => {
            const cc = new CallContext('id', CancellationToken.none);

            const promise = cc.promise;
            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            const brokerResponse = new BrokerMessage.Response(123, null);
            cc.trySet(new Outcome.Succeeded(brokerResponse));

            await Promise.yield();

            fulfilledSpy.should.have.been.called.with(brokerResponse);
        });

        it(`should cause the Promise to be rejected provided an Outcome.Faulted`, async () => {
            const cc = new CallContext('id', CancellationToken.none);

            const promise = cc.promise;
            const rejectedSpy = spy(() => { });
            promise.then(_ => { }, rejectedSpy);

            const error = new Error('some-error');
            cc.trySet(new Outcome.Faulted<BrokerMessage.Response>(error));

            await Promise.yield();

            rejectedSpy.should.have.been.called.with(error);
        });

        it(`should cause the Promise to be rejected provided an Outcome.Canceled`, async () => {
            const cc = new CallContext('id', CancellationToken.none);

            const promise = cc.promise;
            const rejectedSpy = spy((reason: any) => {
                expect(reason).to.be.instanceOf(OperationCanceledError);
            });
            promise.then(_ => { }, rejectedSpy);

            cc.trySet(new Outcome.Canceled<BrokerMessage.Response>());

            await Promise.yield();

            rejectedSpy.should.have.been.called();
        });
    });
});

describe(`core:internals -> class:CallbackContext`, () => {
    context(`ctor`, () => {
        it(`should throw provided a falsy request`, () => {
            (() => new CallbackContext(null as any, async _ => { })).should.throw(ArgumentNullError).with.property('paramName', 'request');
            (() => new CallbackContext(undefined as any, async _ => { })).should.throw(ArgumentNullError).with.property('paramName', 'request');
        });

        it(`should throw provided a falsy _respondAction`, () => {
            const brokerRequest = new BrokerMessage.InboundRequest('methodName', [], 1);

            (() => new CallbackContext(brokerRequest, null as any)).should.throw(ArgumentNullError).with.property('paramName', '_respondAction');
            (() => new CallbackContext(brokerRequest, undefined as any)).should.throw(ArgumentNullError).with.property('paramName', '_respondAction');
        });
    });

    context(`method:respondAsync`, () => {
        it(`shouldn't throw regardless of the response's truthyness`, () => {
            const brokerRequest = new BrokerMessage.InboundRequest('methodName', [], 1);
            const respondAction = async (_: any) => { };
            const cbc = new CallbackContext(brokerRequest, respondAction);

            (() => cbc.respondAsync(null as any)).should.not.throw;
            (() => cbc.respondAsync(undefined as any)).should.not.throw;
            (() => cbc.respondAsync(new BrokerMessage.Response(1, null))).should.not.throw;
        });
        it(`should return a promise which eventually rejects provided a falsy response`, async () => {
            const brokerRequest = new BrokerMessage.InboundRequest('methodName', [], 1);
            const respondAction = async (_: any) => { };
            const cbc = new CallbackContext(brokerRequest, respondAction);

            await cbc.respondAsync(null as any).should.eventually.be.rejectedWith(ArgumentNullError).with.property('paramName', 'response');
            await cbc.respondAsync(undefined as any).should.eventually.be.rejectedWith(ArgumentNullError).with.property('paramName', 'response');
        });

        it(`should return a promise which eventually becomes fulfilled provided a truthy response`, async () => {
            const brokerRequest = new BrokerMessage.InboundRequest('methodName', [], 1);
            const brokerResponse = new BrokerMessage.Response(123, null);

            const respondAction = async (_: any) => { };
            const cbc = new CallbackContext(brokerRequest, respondAction);

            await cbc.respondAsync(brokerResponse).should.eventually.be.fulfilled;
        });

        it(`should cause the provided respondAction to be invoked`, async () => {
            const brokerRequest = new BrokerMessage.InboundRequest('methodName', [], 1);
            const brokerResponse = new BrokerMessage.Response(123, null);

            const respondAction = spy(async (x: BrokerMessage.Response) => {
                expect(x).to.be.equal(brokerResponse);
            });
            const cbc = new CallbackContext(brokerRequest, respondAction);

            await cbc.respondAsync(brokerResponse);
            respondAction.should.have.been.called();
        });
    });
});

describe(`core:internals -> class:CallContextTable`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw`, () => {
            (() => new CallContextTable()).should.not.throw;
        });
    });

    context(`method:createContext`, () => {
        it(`should throw provided a falsy cancellationToken`, () => {
            const cct = new CallContextTable();
            (() => cct.createContext(null as any)).should.throw(ArgumentNullError).with.property('paramName', 'cancellationToken');
        });

        it(`should throw if the CallContextTable had been disposed`, () => {
            const cct = new CallContextTable();
            cct.dispose();
            (() => cct.createContext(CancellationToken.none)).should.throw(ObjectDisposedError);
        });

        it(`should return a truthy reference`, () => {
            const cct = new CallContextTable();
            expect(cct.createContext(CancellationToken.none)).not.to.be.null.and.not.to.be.undefined;
        });
    });

    context(`method:signal`, () => {
        it(`should throw provided a falsy id`, () => {
            const cct = new CallContextTable();
            const outcome = new Outcome.Succeeded<BrokerMessage.Response>(new BrokerMessage.Response(1, null));
            (() => cct.signal(null as any, outcome)).should.throw(ArgumentNullError).with.property('paramName', 'id');
            (() => cct.signal(undefined as any, outcome)).should.throw(ArgumentNullError).with.property('paramName', 'id');
            (() => cct.signal('' as any, outcome)).should.throw(ArgumentNullError).with.property('paramName', 'id');
        });

        it(`should throw provided a falsy outcome`, () => {
            const cct = new CallContextTable();
            (() => cct.signal('id', null as any)).should.throw(ArgumentNullError).with.property('paramName', 'outcome');
            (() => cct.signal('id', undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'outcome');
        });

        it(`should throw if the CallContextTable had been disposed`, () => {
            const cct = new CallContextTable();
            cct.dispose();

            const outcome = new Outcome.Succeeded<BrokerMessage.Response>(new BrokerMessage.Response(1, null));
            (() => cct.signal('id', outcome)).should.throw(ObjectDisposedError);
        });

        it(`shouldn't throw provided truthy but inexistent id`, () => {
            const cct = new CallContextTable();
            const outcome = new Outcome.Succeeded<BrokerMessage.Response>(new BrokerMessage.Response(1, null));

            (() => cct.signal('some-inexistent-id', outcome)).should.not.throw();
        });

        it(`should cause the associated call context's promise to be fulfilled`, async () => {
            const cct = new CallContextTable();
            const brokerResponse = new BrokerMessage.Response(1, null);
            const outcome = new Outcome.Succeeded<BrokerMessage.Response>(brokerResponse);

            const context = cct.createContext(CancellationToken.none);
            cct.signal(context.id, outcome);

            const fulfilledSpy = spy((x: BrokerMessage.Response) => {
                expect(x).to.be.equal(brokerResponse);
            });
            context.promise.then(fulfilledSpy, _ => { });

            await Promise.yield();

            fulfilledSpy.should.have.been.called();
        });
    });

    context(`method:dispose`, () => {
        it(`shouldn't throw even if called multiple times`, () => {
            const cct = new CallContextTable();
            (() => cct.dispose()).should.not.throw();
            (() => cct.dispose()).should.not.throw();
        });

        it(`should cancel all pending call contexts`, async () => {
            const cct = new CallContextTable();

            const context1 = cct.createContext(CancellationToken.none);
            const context2 = cct.createContext(CancellationToken.none);

            cct.dispose();

            const handler = (reason: any) => {
                expect(reason).to.be.instanceOf(OperationCanceledError);
            };

            const rejectedSpy1 = spy(handler);
            const rejectedSpy2 = spy(handler);

            context1.promise.then(_ => { }, rejectedSpy1);
            context2.promise.then(_ => { }, rejectedSpy2);

            await Promise.yield();

            rejectedSpy1.should.have.been.called();
            rejectedSpy2.should.have.been.called();
        });
    });
});
