import {
    ArgumentError,
    ArgumentNullError,
    AsyncAutoResetEvent,
    CancellationTokenSource,
    DispatchProxyClassStore,
    OperationCanceledError,
    PromisePal,
    Timeout,
    TimeSpan,
} from '../../../src/std';

import { expect } from 'chai';
import { PromiseStatus } from '../../../src/std/bcl/promises/PromiseStatus';

describe(`${DispatchProxyClassStore.name}'s`, () => {
    const sut = new DispatchProxyClassStore();

    describe(`🌿 "getOrCreate" method`, () => {
        describe(`should throw when "service" is not a function`, () => {
            const cases = [null, undefined, "foo", true, 123, {}, []];

            for (const _case of cases) {
                it(`like in the case of ${JSON.stringify(_case)}`, () => {
                    const act = () => sut.getOrCreate(_case as any);
                    expect(act).to.throw();
                });
            }
        });

        it(`should not throw when "service" is a function`, () => {
            const act = () => sut.getOrCreate((() => {}) as any);
            expect(act).not.to.throw();
        });

        it(`should return a DispatchProxy class`, () => {
            const Contract = class {}

            const result = sut.getOrCreate(Contract);

            expect(result).to.be.instanceOf(Function);

            let dispatchProxy: any = null;
            const act = () => dispatchProxy = new result(undefined as any);
            expect(act).not.to.throw();

            expect(dispatchProxy).to.be.instanceOf(Contract);
        });

        it(`should return stable DispatchProxy classes` , () => {
            const Contract1 = class {}
            const Contract2 = class {}

            const class1 = sut.getOrCreate(Contract1);
            const class2 = sut.getOrCreate(Contract2);

            expect(class1).to.equal(sut.getOrCreate(Contract1));
            expect(class2).to.equal(sut.getOrCreate(Contract2));

            expect(class1).not.to.equal(class2);
        });
    });
})
