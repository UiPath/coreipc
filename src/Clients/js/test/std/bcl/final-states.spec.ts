import { toJavaScript, calling, context } from '../../infrastructure';
import { FinalState, FinalStateBase } from '../../../src/std';
import { expect } from 'chai';

describe(`surface:foundation`, () => {
    describe(`FinalState`, () => {
        type Case = {
            isRanToCompletion: boolean;
            isFaulted: boolean;
            isCanceled: boolean;
            factory: (...args: any[]) => any;
            factoryArgs: never[];
        };

        const theory = (_case: Case) => {
            const strMethod = `FinalState.${_case.factory.name}`;
            const strArgs = _case.factoryArgs.map(toJavaScript).join(', ');
            const strExpression = `${strMethod}(${strArgs})`;

            context(strExpression, () => {
                it(`should not throw`, () => {
                    calling(_case.factory, ..._case.factoryArgs).should.not.throw();
                });

                it(`should return a FinalState`, () => {
                    expect(_case.factory(..._case.factoryArgs)).to.be.instanceOf(FinalStateBase);
                });

                for (const isProperty of [
                    'isRanToCompletion',
                    'isFaulted',
                    'isCanceled',
                ] as const) {
                    it(`${strExpression}.${isProperty} should return ${toJavaScript(
                        _case[isProperty],
                    )}`, () => {
                        const actual = _case.factory(..._case.factoryArgs)[isProperty]();
                        expect(actual).to.be.eq(_case[isProperty]);
                    });
                }
            });
        };

        theory({
            isRanToCompletion: true,
            isFaulted: false,
            isCanceled: false,
            factory: FinalState.ranToCompletion as (...args: any[]) => any,
            factoryArgs: [123] as never[],
        });

        theory({
            isRanToCompletion: true,
            isFaulted: false,
            isCanceled: false,
            factory: FinalState.ranToCompletion as (...args: any[]) => any,
            factoryArgs: [123] as never[],
        });

        theory({
            isRanToCompletion: false,
            isFaulted: true,
            isCanceled: false,
            factory: FinalState.faulted as (...args: any[]) => any,
            factoryArgs: [new Error()] as never[],
        });

        theory({
            isRanToCompletion: false,
            isFaulted: false,
            isCanceled: true,
            factory: FinalState.canceled as (...args: any[]) => any,
            factoryArgs: [] as never[],
        });
    });
});
