import { expect, constructing, toJavaScript } from '@test-helpers';
import { ObjectDisposedError, ArgumentOutOfRangeError } from '@foundation';

describe(`surface:foundation`, () => {
    describe(`ObjectDisposedError`, () => {
        context(`the constructor`, () => {
            it(`should not throw`, () => {
                for (const args of [
                    [],
                    [undefined],
                    ['some object'],
                    [undefined, 'some message'],
                    ['some object', 'some message'],
                ]) {

                    constructing(ObjectDisposedError, ...args)
                        .should.not.throw();

                }
            });

            it(`should throw if called with an argument for objectName which isn't a string`, () => {
                for (const args of [
                    [123],
                    [true],
                    [() => { }],
                    [Symbol()],
                    [[]],
                    [{}],
                    [123, 'some message'],
                    [true, 'some message'],
                    [() => { }, 'some message'],
                    [Symbol(), 'some message'],
                    [[], 'some message'],
                    [{}, 'some message'],
                ] as never[][]) {

                    constructing(ObjectDisposedError, ...args)
                        .should.throw(ArgumentOutOfRangeError, `Specified argument's type was neither of: 'undefined', 'string'.`)
                        .with.property('paramName', 'objectName');

                }
            });

            it(`should throw if called with an argument for message which isn't a string`, () => {
                for (const args of [
                    [undefined, 123],
                    [undefined, true],
                    [undefined, () => { }],
                    [undefined, Symbol()],
                    [undefined, []],
                    [undefined, {}],
                    ['some object', 123],
                    ['some object', true],
                    ['some object', () => { }],
                    ['some object', Symbol()],
                    ['some object', []],
                    ['some object', {}],
                ] as never[][]) {

                    constructing(ObjectDisposedError, ...args)
                        .should.throw(ArgumentOutOfRangeError, `Specified argument's type was neither of: 'undefined', 'string'.`)
                        .with.property('paramName', 'message');

                }
            });

            context(`should set objectName accordingly`, () => {
                for (const _case of [
                    { expected: null, args: [] },
                    { expected: null, args: [undefined, undefined] },
                    { expected: null, args: [undefined, 'message'] },
                    { expected: 'xy', args: ['xy'] },
                    { expected: 'xy', args: ['xy', undefined] },
                    { expected: 'xy', args: ['xy', 'message'] },
                ]) {
                    const concatenatedArgs = _case.args.map(toJavaScript).join(', ');
                    it(`should set objectName to ${toJavaScript(_case.expected)} when called with: (${concatenatedArgs})`, () => {
                        expect(new ObjectDisposedError(..._case.args).objectName).to.be.eq(_case.expected);
                    });
                }
            });

            context(`should set message accordingly`, () => {
                const defaultMessage = 'Cannot access a disposed object.';

                for (const _case of [
                    { expected: defaultMessage, args: [] },
                    { expected: 'custom message', args: [undefined, 'custom message'] },
                    { expected: `${defaultMessage}\r\nObject name: 'xy'.`, args: ['xy'] },
                    { expected: `custom message\r\nObject name: 'xy'.`, args: ['xy', 'custom message'] },
                ]) {
                    const concatenatedArgs = _case.args.map(toJavaScript).join(', ');
                    it(`should set message to ${toJavaScript(_case.expected)} when called with: (${concatenatedArgs})`, () => {
                        expect(new ObjectDisposedError(..._case.args).message)
                            .to.be.eq(_case.expected);
                    });
                }
            });
        });
    });
});
