import { constructing, context } from '../../infrastructure';
import { ArgumentNullError } from '../../../src/std';
import { expect } from 'chai';

describe(`${ArgumentNullError.name}'s`, () => {
    context(`ctor`, () => {
        it(`should not throw`, () => {
            for (const paramName of paramNames) {
                for (const message of messages) {
                    constructing(ArgumentNullError, paramName, message).should.not.throw();
                }
            }
        });

        it(`should set the paramName property accordingly`, () => {
            for (const paramName of paramNames) {
                for (const message of messages) {
                    expect(new ArgumentNullError(paramName, message).paramName).to.be.eq(paramName);
                }
            }
        });

        it(`should set the message property accordingly`, () => {
            expect(new ArgumentNullError().message).to.be.eq('Value cannot be null.');
            expect(new ArgumentNullError('someParam').message).to.be.eq(
                `Value cannot be null. (Parameter: 'someParam')`,
            );
            expect(new ArgumentNullError('someParam', 'someMessage').message).to.be.eq(
                `someMessage (Parameter: 'someParam')`,
            );
            expect(new ArgumentNullError(undefined, 'someMessage').message).to.be.eq(`someMessage`);
        });

        const paramNames = ['someName', null, undefined, ''] as never[];
        const messages = ['some message', null, undefined, ''] as never[];
    });
});
