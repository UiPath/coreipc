import { expect, constructing, toJavaScript } from '@test-helpers';
import { AggregateError, ArgumentOutOfRangeError, ArgumentError, InvalidOperationError } from '@foundation';

describe(`surface:foundation`, () => {
    describe(`AggregateError`, () => {
        context(`the constructor`, () => {
            context(`should not throw`, () => {
                for (const args of [
                    [],
                    [undefined, new Error()],
                    [undefined, new Error(), new ArgumentError()],
                    ['some message'],
                    ['some message', new Error(), new ArgumentError()],
                ] as any[][]) {
                    it(`should not throw when called with: (${args.map(toJavaScript).join(', ')})`, () => {
                        constructing(ArgumentError, ...args).should.not.throw();
                    });
                }
            });

            it(`should throw if called with an argument for message which isn't a string`, () => {
                for (const args of [
                    [123],
                    [true],
                    [() => { }],
                    [Symbol()],
                    [[]],
                    [{}],
                    [123, new Error()],
                    [true, new Error()],
                    [() => { }, new Error()],
                    [Symbol(), new Error()],
                    [[], new Error()],
                    [{}, new Error()],
                ] as never[][]) {

                    constructing(AggregateError, ...args)
                        .should.throw(ArgumentOutOfRangeError, `Specified argument's type was neither of: 'undefined', 'string'.`)
                        .with.property('paramName', 'message');

                }
            });

            context(`should throw if called with a secondary argument which isn't an Error`, () => {
                for (const args of [
                    [undefined, 123],
                    [undefined, true],
                    [undefined, () => { }],
                    [undefined, Symbol()],
                    [undefined, []],
                    [undefined, {}],
                    ['some message', 123],
                    ['some message', true],
                    ['some message', () => { }],
                    ['some message', Symbol()],
                    ['some message', []],
                    ['some message', {}],
                    [undefined, new Error(), 123],
                    [undefined, new Error(), true],
                    [undefined, new Error(), () => { }],
                    [undefined, new Error(), Symbol()],
                    [undefined, new Error(), []],
                    [undefined, new Error(), {}],
                    ['some message', new Error(), 123],
                    ['some message', new Error(), true],
                    ['some message', new Error(), () => { }],
                    ['some message', new Error(), Symbol()],
                    ['some message', new Error(), []],
                    ['some message', new Error(), {}],
                ] as never[][]) {
                    it(`should throw when called with: ${args.map(toJavaScript).join(', ')}`, () => {

                        constructing(AggregateError, ...args)
                            .should.throw(ArgumentOutOfRangeError, `Specified argument contained at least one element which is not an Error.`)
                            .with.property('paramName', 'errors');

                    });
                }
            });

            context(`should set errors accordingly`, () => {
                for (const args of [
                    [undefined, new Error()],
                    [undefined, new Error(), new ArgumentError()],
                    [undefined, new Error(), new ArgumentError(), new InvalidOperationError()],
                    ['some message', new Error()],
                    ['some message', new Error(), new ArgumentError()],
                    ['some message', new Error(), new ArgumentError(), new InvalidOperationError()],
                ] as never[][]) {
                    it(`should set errors to [${args.slice(1).map(toJavaScript).join(', ')}] when called with: (${args.map(toJavaScript).join(', ')})`, () => {
                        expect(new AggregateError(...args).errors)
                            .to.be.deep.eq(args.slice(1));
                    });
                }
            });

            context(`should set message accordingly`, () => {
                for (const _case of [
                    { expected: 'One or more errors occurred.', args: [] },
                    { expected: 'xy', args: ['xy'] },
                    { expected: 'One or more errors occurred. (Foo)', args: [undefined, new Error('Foo')] },
                    { expected: 'xy (Foo)', args: ['xy', new Error('Foo')] },
                    { expected: 'One or more errors occurred. (Foo) (Bar)', args: [undefined, new Error('Foo'), new InvalidOperationError('Bar')] },
                    { expected: 'xy (Foo) (Bar)', args: ['xy', new Error('Foo'), new InvalidOperationError('Bar')] },
                ] as Array<{ expected: string, args: never[] }>) {

                    const concatenatedArgs = _case.args.map(toJavaScript).join(', ');

                    it(`should set objectName to ${toJavaScript(_case.expected)} when called with: (${concatenatedArgs})`, () => {
                        expect(new AggregateError(..._case.args).message).to.be.eq(_case.expected);
                    });
                }
            });
        });
    });
});
