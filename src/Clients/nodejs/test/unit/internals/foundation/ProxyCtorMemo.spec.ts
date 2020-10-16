import { constructing, forInstance, toJavaScript } from '@test-helpers';
import { ProxyCtorMemo, ArgumentNullError, ArgumentOutOfRangeError } from '@foundation';

describe(`internals`, () => {
    describe(`ProxyCtorMemo`, () => {
        context(`the constructor`, () => {
            it(`should not throw`, () => {
                constructing(ProxyCtorMemo).should.not.throw();
            });
        });

        context(`the get method`, () => {
            it(`should not throw when called with a function`, () => {
                const memo = new ProxyCtorMemo();
                class Contract { }
                forInstance(memo).calling('get', Contract).should.not.throw();
            });

            context(`should throw when called with invalid args`, () => {
                for (const args of [
                    [],
                    [undefined],
                    [null],
                ] as never[][]) {
                    it(`get(${args.map(toJavaScript).join(', ')}) should throw ArgumentNullError`, () => {
                        const memo = new ProxyCtorMemo();
                        forInstance(memo).callingWrong('get', ...args).should.throw(ArgumentNullError);
                    });
                }
                for (const args of [
                    [0],
                    [1],
                    [false],
                    [true],
                    ['some string'],
                    [Symbol()],
                    [{}],
                    [[]],
                ] as never[][]) {
                    it(`get(${args.map(toJavaScript).join(', ')}) should throw ArgumentOutOfRangeError`, () => {
                        const memo = new ProxyCtorMemo();
                        forInstance(memo).callingWrong('get', ...args).should.throw(ArgumentOutOfRangeError);
                    });
                }
            });

            it(`should return the same object provided the same class`, () => {
                const memo = new ProxyCtorMemo();
                class Contract { }
                memo.get(Contract).should.be.eq(memo.get(Contract));;
            });
        });
    });
});
