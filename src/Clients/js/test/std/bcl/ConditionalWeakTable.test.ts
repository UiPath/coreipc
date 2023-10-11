import {
    ArgumentNullError,
    AsyncAutoResetEvent,
    CancellationTokenSource,
    ConditionalWeakTable,
    OperationCanceledError,
    PromisePal,
    Timeout,
    TimeSpan,
} from '../../../src/std';

import { expect } from 'chai';
import { PromiseStatus } from '../../../src/std/bcl/promises/PromiseStatus';

describe(`${ConditionalWeakTable.name}'s`, () => {
    const sut = new ConditionalWeakTable<any, any>();

    describe(`🌿 "getOrCreateValue" method`, () => {
        describe(`should not throw when called with valid args`, () => {
            const cases = [
                [{}, (_: any) => "test"] as const,
                [() => { }, (_: any) => "test"] as const,
            ];

            for (const [key, valueFactory] of cases) {
                it(`like ${JSON.stringify([key, valueFactory])}`, () => {
                    const act = () => sut.getOrCreateValue(key, valueFactory);
                    expect(act).to.not.throw();
                });
            }
        });

        it(`should return consistent results`, () => {
            const SomeClass = class { };
            const result = {};

            let callCount = 0;
            const factory = () => {
                callCount++;
                return result;
            }

            expect(sut.getOrCreateValue(SomeClass, factory)).to.equal(result);
            expect(sut.getOrCreateValue(SomeClass, factory)).to.equal(result);
            expect(callCount).to.equal(1);
        });
    });
});
