import { expect, constructing } from '@test-helpers';
import { TimeoutError } from '@foundation';

describe(`surface:foundation`, () => {
    describe(`TimeoutError`, () => {
        context(`the constructor`, () => {
            it(`should not throw`, () => {
                constructing(TimeoutError).should.not.throw();
            });

            it(`should set the message property accordingly`, () => {
                expect(new TimeoutError().message).to.be.eq('The operation has timed out.');
            });
        });
    });
});
