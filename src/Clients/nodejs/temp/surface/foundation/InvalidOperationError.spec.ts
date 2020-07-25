import { expect, constructing } from '@test-helpers';
import { InvalidOperationError, ArgumentOutOfRangeError } from '@foundation';

describe(`surface:foundation`, () => {
    describe(`InvalidOperationError`, () => {
        context(`the constructor`, () => {
            it(`should not throw`, () => {
                for (const args of [
                    [],
                    [undefined],
                    ['some message'],
                ]) {

                    constructing(InvalidOperationError, ...args)
                        .should.not.throw();

                }
            });

            it(`should throw when called with something other than a string`, () => {
                for (const args of [
                    [123],
                    [true],
                    [() => { }],
                    [Symbol()],
                    [[]],
                ] as never[][]) {

                    constructing(InvalidOperationError, ...args)
                        .should.throw(ArgumentOutOfRangeError, `Specified argument's type was neither of: 'undefined', 'string'.`)
                        .with.property('paramName', 'message');

                }
            });

            it(`should set the message accordingly`, () => {
                for (const arg of ['some message', 'some other message']) {
                    expect(new InvalidOperationError(arg).message).to.be.eq(arg);
                }
            });

            it(`should fallback on the default message when no message is provided`, () => {
                for (const args of [
                    [],
                    [undefined],
                ]) {
                    expect(new InvalidOperationError(...args).message).to.be.eq('Operation is not valid due to the current state of the object.');
                }
            });
        });
    });
});
