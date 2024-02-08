import { RandomCancellationToken } from '../../../src/std';

import { expect } from 'chai';

describe(`${RandomCancellationToken.name}'s`, () => {
    describe(`📞 "toString" static method`, () => {
        it(`should not throw`, () => {
            const act = () => RandomCancellationToken.toString();
            expect(act).not.to.throw();
        });

        it(`should return "new CancellationToken()"`, () => {
            expect(RandomCancellationToken.toString()).to.equal('new CancellationToken()');
        });
    });
});
