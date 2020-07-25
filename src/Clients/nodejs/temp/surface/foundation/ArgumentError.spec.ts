import { expect, constructing } from '@test-helpers';
import { ArgumentError } from '@foundation';

describe(`surface:foundation`, () => {
    describe(`ArgumentError`, () => {
        context(`the constructor`, () => {
            it(`should not throw`, () => {
                for (const paramName of paramNames) {
                    for (const message of messages) {
                        constructing(ArgumentError, paramName, message)
                            .should.not.throw();
                    }
                }
            });

            it(`should set the paramName property accordingly`, () => {
                for (const paramName of paramNames) {
                    for (const message of messages) {
                        expect(new ArgumentError(message, paramName).paramName).to.be.eq(paramName);
                    }
                }
            });

            it(`should set the message property accordingly`, () => {
                expect(new ArgumentError().message).to.be.eq('Value does not fall within the expected range.');
                expect(new ArgumentError('someMessage').message).to.be.eq(`someMessage`);
                expect(new ArgumentError('someMessage', 'someParam').message).to.be.eq(`someMessage (Parameter: 'someParam')`);
                expect(new ArgumentError(undefined, 'someParam').message).to.be.eq(`Value does not fall within the expected range. (Parameter: 'someParam')`);
            });

            const paramNames = ['someName', null, undefined, ''] as never[];
            const messages = ['some message', null, undefined, ''] as never[];
        });
    });
});
