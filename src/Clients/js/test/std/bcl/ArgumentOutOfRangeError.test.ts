import { constructing, context } from '../../infrastructure';
import { ArgumentOutOfRangeError } from '../../../src/std';
import { expect } from 'chai';

describe(`${ArgumentOutOfRangeError.name}'s`, () => {
    context(`ctor`, () => {
        it(`should not throw`, () => {
            for (const paramName of paramNames) {
                for (const message of messages) {
                    constructing(ArgumentOutOfRangeError, paramName, message).should.not.throw();
                }
            }
        });

        it(`should set the paramName property accordingly`, () => {
            for (const paramName of paramNames) {
                for (const message of messages) {
                    expect(new ArgumentOutOfRangeError(paramName, message).paramName).to.be.eq(
                        paramName,
                    );
                }
            }
        });

        it(`should set the message property accordingly`, () => {
            expect(new ArgumentOutOfRangeError().message).to.be.eq(
                'Specified argument was out of the range of valid values.',
            );
            expect(new ArgumentOutOfRangeError('someParam').message).to.be.eq(
                `Specified argument was out of the range of valid values. (Parameter: 'someParam')`,
            );
            expect(new ArgumentOutOfRangeError('someParam', 'someMessage').message).to.be.eq(
                `someMessage (Parameter: 'someParam')`,
            );
            expect(new ArgumentOutOfRangeError(undefined, 'someMessage').message).to.be.eq(
                `someMessage`,
            );
        });

        const paramNames = ['someName', null, undefined, ''] as never[];
        const messages = ['some message', null, undefined, ''] as never[];
    });
});
