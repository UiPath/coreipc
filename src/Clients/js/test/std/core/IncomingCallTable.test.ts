import { CancellationToken, IncomingCallTable } from '../../../src/std';

import { expect } from 'chai';

describe(`${IncomingCallTable.name}`, () => {
    it('register yields a live (uncancelled) token', () => {
        const table = new IncomingCallTable();

        const ct = table.register('1');

        expect(ct.isCancellationRequested).to.equal(false);
    });

    it('tryCancel cancels the token (and fires registrations) of the matching call', () => {
        const table = new IncomingCallTable();
        const ct = table.register('1');

        let fired = false;
        ct.register(() => {
            fired = true;
        });

        table.tryCancel('1');

        expect(ct.isCancellationRequested).to.equal(true);
        expect(fired).to.equal(true);
    });

    it('tryCancel is a harmless no-op for an unknown id', () => {
        const table = new IncomingCallTable();
        const ct = table.register('1');

        expect(() => table.tryCancel('does-not-exist')).to.not.throw();
        expect(ct.isCancellationRequested).to.equal(false);
    });

    it('tryCancel twice does not throw and stays cancelled', () => {
        const table = new IncomingCallTable();
        const ct = table.register('1');

        table.tryCancel('1');
        expect(() => table.tryCancel('1')).to.not.throw();

        expect(ct.isCancellationRequested).to.equal(true);
    });

    it('complete stops tracking so a later cancel is a no-op', () => {
        const table = new IncomingCallTable();
        const ct = table.register('1');

        table.complete('1');
        table.tryCancel('1');

        expect(ct.isCancellationRequested).to.equal(false);
    });

    it('complete for an unknown id does not throw', () => {
        const table = new IncomingCallTable();

        expect(() => table.complete('nope')).to.not.throw();
    });

    it('clear cancels every tracked call', () => {
        const table = new IncomingCallTable();
        const a = table.register('a');
        const b = table.register('b');

        table.clear();

        expect(a.isCancellationRequested).to.equal(true);
        expect(b.isCancellationRequested).to.equal(true);
    });

    it('distinct ids are cancelled independently', () => {
        const table = new IncomingCallTable();
        const a = table.register('a');
        const b = table.register('b');

        table.tryCancel('a');

        expect(a.isCancellationRequested).to.equal(true);
        expect(b.isCancellationRequested).to.equal(false);
    });
});
