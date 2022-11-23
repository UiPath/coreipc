import {
    AggregateError,
    ArgumentOutOfRangeError,
    ArgumentError,
    InvalidOperationError,
} from '../../../src/std';

import { expect } from 'chai';
import { _jsargs, __for, __fact } from '../../infrastructure';

__for(`${AggregateError.name}'s`, () => {
    __for(`ctor`, () => {
        __for(`should not throw`, () => {
            for (const args of [
                [],
                [undefined, new Error()],
                [undefined, new Error(), new ArgumentError()],
                ['some message'],
                ['some message', new Error(), new ArgumentError()],
            ] as ConstructorParameters<typeof AggregateError>[]) {
                __fact(`should not throw when called with: (${_jsargs(args)})`, () => {
                    const act = () => new AggregateError(...args);

                    expect(act).not.to.throw();
                });
            }
        });

        __for(`should throw if called with an invalid message`, () => {
            for (const args of [
                [123],
                [true],
                [() => {}],
                [Symbol()],
                [[]],
                [{}],
                [123, new Error()],
                [true, new Error()],
                [() => {}, new Error()],
                [Symbol(), new Error()],
                [[], new Error()],
                [{}, new Error()],
            ] as any as ConstructorParameters<typeof AggregateError>[]) {
                __fact(`with args ${_jsargs(args)}`, () => {
                    const act = () => new AggregateError(...args);

                    expect(act)
                        .to.throw(
                            ArgumentOutOfRangeError,
                            `Specified argument's type was neither of: 'undefined', 'string'.`,
                        )
                        .with.property('paramName', 'message');
                });
            }
        });

        __for(`should throw if called with a secondary argument which isn't an Error`, () => {
            for (const args of [
                [undefined, 123],
                [undefined, true],
                [undefined, () => {}],
                [undefined, Symbol()],
                [undefined, []],
                [undefined, {}],
                ['some message', 123],
                ['some message', true],
                ['some message', () => {}],
                ['some message', Symbol()],
                ['some message', []],
                ['some message', {}],
                [undefined, new Error(), 123],
                [undefined, new Error(), true],
                [undefined, new Error(), () => {}],
                [undefined, new Error(), Symbol()],
                [undefined, new Error(), []],
                [undefined, new Error(), {}],
                ['some message', new Error(), 123],
                ['some message', new Error(), true],
                ['some message', new Error(), () => {}],
                ['some message', new Error(), Symbol()],
                ['some message', new Error(), []],
                ['some message', new Error(), {}],
            ] as ConstructorParameters<typeof AggregateError>[]) {
                __fact(`should throw when called with: ${_jsargs(args)}`, () => {
                    const act = () => new AggregateError(...args);
                    expect(act)
                        .to.throw(
                            ArgumentOutOfRangeError,
                            `Specified argument contained at least one element which is not an Error.`,
                        )
                        .with.property('paramName', 'errors');
                });
            }
        });

        __for(`should set errors accordingly`, () => {
            for (const args of [
                [undefined, new Error()],
                [undefined, new Error(), new ArgumentError()],
                [undefined, new Error(), new ArgumentError(), new InvalidOperationError()],
                ['some message', new Error()],
                ['some message', new Error(), new ArgumentError()],
                ['some message', new Error(), new ArgumentError(), new InvalidOperationError()],
            ] as ConstructorParameters<typeof AggregateError>[]) {
                __fact(`should set errors to [${JSON.stringify(
                    args.slice(1),
                )}] when called with: (${_jsargs(args)})`, () => {
                    expect(new AggregateError(...args).errors).to.be.deep.eq(args.slice(1));
                });
            }
        });

        __for(`should set message accordingly`, () => {
            for (const _case of [
                { expected: 'One or more errors occurred.', args: [] },
                { expected: 'xy', args: ['xy'] },
                {
                    expected: 'One or more errors occurred. (Foo)',
                    args: [undefined, new Error('Foo')],
                },
                { expected: 'xy (Foo)', args: ['xy', new Error('Foo')] },
                {
                    expected: 'One or more errors occurred. (Foo) (Bar)',
                    args: [undefined, new Error('Foo'), new InvalidOperationError('Bar')],
                },
                {
                    expected: 'xy (Foo) (Bar)',
                    args: ['xy', new Error('Foo'), new InvalidOperationError('Bar')],
                },
            ] as Array<{ expected: string; args: ConstructorParameters<typeof AggregateError> }>) {
                const concatenatedArgs = JSON.stringify(_case.args);

                __fact(`should set objectName to ${JSON.stringify(
                    _case.expected,
                )} when called with: (${concatenatedArgs})`, () => {
                    const sut = new AggregateError(..._case.args);

                    expect(sut.message).to.be.eq(_case.expected);
                });
            }
        });
    });

    __for(`📞 "maybeAggregate" static method`, () => {
        __for(`should not throw when called with valid args`, () => {
            const theory = (...args: Parameters<typeof AggregateError.maybeAggregate>): void => {
                __fact(`args === ${_jsargs(args)}`, () => {
                    const act = () => AggregateError.maybeAggregate(...args);

                    expect(act).not.to.throw();
                });
            };

            theory();
            theory(new Error());
            theory(new Error(), new Error());
            theory(new Error(), new Error(), new Error());
        });

        __for(`should throw when called with invalid args`, () => {
            const theory = (...invalidArgs: any[]): void => {
                const args: Parameters<typeof AggregateError.maybeAggregate> = invalidArgs;

                __fact(`args === ${_jsargs(args)}`, () => {
                    const act = () => AggregateError.maybeAggregate(...args);

                    expect(act).to.throw(ArgumentOutOfRangeError);
                });
            };

            theory(123);
            theory(true);
            theory({});
            theory(123, true);
            theory(123, true, {});
        });

        __fact(`should return undefined when called with no errors`, () => {
            expect(AggregateError.maybeAggregate()).to.equal(undefined);
        });

        __fact(`should return the single error it received when called with one error`, () => {
            const singleError = new Error();
            const actual = AggregateError.maybeAggregate(singleError);

            expect(actual).to.equal(singleError);
        });

        __fact(`should return an ${AggregateError.name} that contains the received errors when called with more than one error`, () => {
            const error1 = new Error();
            const error2 = new Error();
            const actual = AggregateError.maybeAggregate(error1, error2);

            expect(actual)
                .to.be.instanceOf(AggregateError)
                .that.satisfies((x: any) => {
                    const specific = x as AggregateError;

                    expect(specific.errors).to.have.lengthOf(2);
                    expect(specific.errors).to.contain(error1).and.to.contain(error2);

                    return true;
                });
        });
    });
});
