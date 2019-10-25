// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';

import { IpcClient, IIpcClientConfig } from '@core/surface';
import { IpcClientConfig } from '@core/surface/ipc-client';
import { IBroker } from '@core/internals/broker';
import { ArgumentNullError } from '@foundation/errors';
import { PromiseCompletionSource } from '@foundation/threading';

import * as BrokerMessage from '@core/internals/broker-message';

use(spies);

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
