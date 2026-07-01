import { AbortSignalAdapter, CancellationToken, CancellationTokenSource } from '../../../src/std';

import { expect } from 'chai';

describe(`${AbortSignalAdapter.name}`, () => {
    describe('isAbortSignal', () => {
        it('recognizes an AbortSignal and rejects everything else', () => {
            const ac = new AbortController();
            expect(AbortSignalAdapter.isAbortSignal(ac.signal)).to.equal(true);
            expect(AbortSignalAdapter.isAbortSignal(CancellationToken.none)).to.equal(false);
            expect(AbortSignalAdapter.isAbortSignal(undefined)).to.equal(false);
            expect(AbortSignalAdapter.isAbortSignal(null)).to.equal(false);
            expect(AbortSignalAdapter.isAbortSignal({})).to.equal(false);
        });
    });

    describe('toCancellationToken', () => {
        it('yields a token that is not yet cancelled for a live signal', () => {
            const ac = new AbortController();
            const ct = AbortSignalAdapter.toCancellationToken(ac.signal);
            expect(ct.isCancellationRequested).to.equal(false);
        });

        it('cancels the token (and fires registrations) when the signal aborts', () => {
            const ac = new AbortController();
            const ct = AbortSignalAdapter.toCancellationToken(ac.signal);

            let fired = false;
            ct.register(() => {
                fired = true;
            });

            ac.abort();

            expect(ct.isCancellationRequested).to.equal(true);
            expect(fired).to.equal(true);
        });

        it('yields an already-cancelled token for an already-aborted signal', () => {
            const ac = new AbortController();
            ac.abort();

            const ct = AbortSignalAdapter.toCancellationToken(ac.signal);

            expect(ct.isCancellationRequested).to.equal(true);
        });
    });

    describe('ensureCancellationToken', () => {
        it('passes a CancellationToken through unchanged', () => {
            const ct = CancellationToken.none;
            expect(AbortSignalAdapter.ensureCancellationToken(ct)).to.equal(ct);
        });

        it('adapts an AbortSignal to a CancellationToken', () => {
            const ac = new AbortController();
            const token = AbortSignalAdapter.ensureCancellationToken(ac.signal);

            expect(token).to.be.instanceOf(CancellationToken);
            expect(token!.isCancellationRequested).to.equal(false);

            ac.abort();

            expect(token!.isCancellationRequested).to.equal(true);
        });

        it('returns undefined for a non-cancellation value', () => {
            expect(AbortSignalAdapter.ensureCancellationToken({})).to.equal(undefined);
            expect(AbortSignalAdapter.ensureCancellationToken(42)).to.equal(undefined);
            expect(AbortSignalAdapter.ensureCancellationToken(undefined)).to.equal(undefined);
        });
    });

    describe('toAbortSignal', () => {
        it('yields a signal that is not yet aborted for a live token', () => {
            const cts = new CancellationTokenSource();
            const signal = AbortSignalAdapter.toAbortSignal(cts.token);
            expect(signal).to.be.instanceOf(AbortSignal);
            expect(signal.aborted).to.equal(false);
        });

        it('aborts the signal when the token is cancelled', () => {
            const cts = new CancellationTokenSource();
            const signal = AbortSignalAdapter.toAbortSignal(cts.token);

            cts.cancel();

            expect(signal.aborted).to.equal(true);
        });

        it('yields an already-aborted signal for an already-cancelled token', () => {
            const cts = new CancellationTokenSource();
            cts.cancel();

            const signal = AbortSignalAdapter.toAbortSignal(cts.token);

            expect(signal.aborted).to.equal(true);
        });

        it('round-trips CancellationToken -> AbortSignal -> CancellationToken', () => {
            const cts = new CancellationTokenSource();
            const signal = AbortSignalAdapter.toAbortSignal(cts.token);
            const ct = AbortSignalAdapter.toCancellationToken(signal);

            expect(ct.isCancellationRequested).to.equal(false);
            cts.cancel();
            expect(ct.isCancellationRequested).to.equal(true);
        });
    });
});
