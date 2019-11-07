// tslint:disable: max-line-length
// tslint:disable: no-unused-expression

import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';
import chaiAsPromised from 'chai-as-promised';

use(spies);
use(chaiAsPromised);

import { IpcClient } from '../../../src/core/surface';
import { IpcClientConfig } from '../../../src/core/surface/ipc-client';
import { IBroker } from '../../../src/core/internals/broker';
import { ArgumentNullError } from '../../../src/foundation/errors';
import { PromiseCompletionSource, CancellationToken } from '../../../src/foundation/threading';
import { IPipeClientStream } from '../../../src/foundation/pipes';

import * as BrokerMessage from '../../../src/core/internals/broker-message';

describe(`core:surface -> class:IpcClient`, () => {
    context(`ctor`, () => {
        it(`should throw provided a falsy pipeName`, () => {
            (() => new IpcClient(null as any, Object)).should.throw(ArgumentNullError).with.property('paramName', 'pipeName');
            (() => new IpcClient(undefined as any, Object)).should.throw(ArgumentNullError).with.property('paramName', 'pipeName');
            (() => new IpcClient('', Object)).should.throw(ArgumentNullError).with.property('paramName', 'pipeName');
        });

        it(`should throw provided a falsy serviceCtor`, () => {
            (() => new IpcClient('pipeName', null as any)).should.throw(ArgumentNullError).with.property('paramName', 'serviceCtor');
            (() => new IpcClient('pipeName', undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'serviceCtor');
        });

        it(`shouldn't throw provided valid args`, () => {
            (() => new IpcClient('pipeName', Object)).should.not.throw();
            (() => new IpcClient('pipeName', Object, config => { })).should.not.throw();
        });
    });

    context(`property:proxy`, () => {
        it(`shouldn't throw`, () => {
            (() => new IpcClient('pipeName', Object).proxy).should.not.throw();
        });

        it(`shouldn't be null or undefined`, () => {
            expect(new IpcClient('pipeName', Object).proxy).not.to.be.null.and.not.to.be.undefined;
        });

        it(`should return the same object over and over`, () => {
            const ipcClient = new IpcClient('pipeName', Object);
            ipcClient.proxy.should.equal(ipcClient.proxy);
        });

        it(`should return a transparent proxy which marshalls through the underlying broker`, async () => {
            class IContract {
                public sumAsync(x: number, y: number): Promise<number> { throw null; }
            }

            const pcsCall = new PromiseCompletionSource<{
                brokerRequest: BrokerMessage.Request
                pcsRespond: PromiseCompletionSource<BrokerMessage.Response>
            }>();

            const mockBroker: IBroker = {
                async sendReceiveAsync(brokerRequest: BrokerMessage.Request) {
                    const pcsRespond = new PromiseCompletionSource<BrokerMessage.Response>();
                    pcsCall.setResult({ brokerRequest, pcsRespond });
                    return await pcsRespond.promise;
                },
                async disposeAsync() {
                }
            };

            const ipcClient = new IpcClient(
                'pipeName',
                IContract,
                config => {
                    (config as any as IpcClientConfig).maybeBroker = mockBroker;
                }
            );

            let promise: Promise<number> = null as any;
            (() => promise = ipcClient.proxy.sumAsync(1, 2)).should.not.throw();

            expect(promise).to.be.instanceOf(Promise);

            const spyPcsCallFulfilled = spy(() => { });
            pcsCall.promise.then(spyPcsCallFulfilled);
            await Promise.yield();
            spyPcsCallFulfilled.should.have.been.called();

            const obj = await pcsCall.promise;
            expect(obj).not.to.be.null.and.not.be.undefined;
            expect(obj.brokerRequest).to.be.instanceOf(BrokerMessage.OutboundRequest);
            expect(obj.pcsRespond).to.be.instanceOf(PromiseCompletionSource);
            expect(obj.brokerRequest.methodName).to.equal('sumAsync');
            expect(obj.brokerRequest.args).to.deep.equal([1, 2]);

            obj.pcsRespond.setResult(new BrokerMessage.Response(3, null));
            await promise.should.eventually.be.fulfilled.and.be.equal(3);
        });
    });

    context(`method:closeAsync`, () => {
        it(`shouldn't reject even if called multiple times`, async () => {
            const ipcClient = new IpcClient('pipeName', Object);
            await ipcClient.closeAsync().should.eventually.be.fulfilled;
            await ipcClient.closeAsync().should.eventually.be.fulfilled;
        });
    });
});

describe(`core:surface -> class:IpcClientConfig`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw`, () => {
            (() => new IpcClientConfig()).should.not.throw();
        });
    });

    context(`method:setConnectionFactory`, () => {
        it(`should throw provided a falsy delegate`, () => {
            const instance = new IpcClientConfig();

            (() => instance.setConnectionFactory(null as any)).should.throw(ArgumentNullError).with.property('paramName', 'delegate');
            (() => instance.setConnectionFactory(undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'delegate');
        });

        it(`shouldn't throw provided a valid delegate`, () => {
            const instance = new IpcClientConfig();

            (() => instance.setConnectionFactory(async (connect, ct) => { })).should.not.throw();
        });

        it(`should set the maybeConnectionFactory field`, () => {
            const instance = new IpcClientConfig();
            const method = async (connect: () => Promise<IPipeClientStream>, cancellationToken: CancellationToken): Promise<IPipeClientStream | void> => { };

            instance.setConnectionFactory(method);
            expect(instance.maybeConnectionFactory).to.equal(method);
        });
    });

    context(`method:setBeforeCall`, () => {
        it(`should throw provided a falsy delegate`, () => {
            const instance = new IpcClientConfig();

            (() => instance.setBeforeCall(null as any)).should.throw(ArgumentNullError).with.property('paramName', 'delegate');
            (() => instance.setBeforeCall(undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'delegate');
        });

        it(`shouldn't throw provided a valid delegate`, () => {
            const instance = new IpcClientConfig();

            (() => instance.setBeforeCall(async (connect, ct) => { })).should.not.throw();
        });

        it(`should set the maybeBeforeCall field`, () => {
            const instance = new IpcClientConfig();
            const method = async (methodName: string, newConnection: boolean, cancellationToken: CancellationToken): Promise<void> => { };

            instance.setBeforeCall(method);
            expect(instance.maybeBeforeCall).to.equal(method);
        });
    });
});
