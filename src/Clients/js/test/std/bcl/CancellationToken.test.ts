import {
    CancellationToken,
    CancellationTokenSource,
    OperationCanceledError,
    PromiseCompletionSource,
    PromisePal,
} from '../../../src/std';

import { expect } from 'chai';

import pluralize from 'pluralize';
import { ArgumentOutOfRangeError } from 'rxjs';

const _nameof = <T>(name: keyof T) => name;

describe(`${CancellationToken.name}'s`, () => {
    const __ = (name: keyof CancellationToken) => name;

    describe(`"${__('bind')}" method`, () => {
        it(`🚡 PromiseCompletionSource should not leave unobserved rejections`, async () => {
            const pcs = new PromiseCompletionSource<void>();

            PromisePal.traceError(pcs.promise);

            pcs.setCanceled();
        });

        it(`🚡 CancellationToken.bind should not cause unobserved rejections`, async () => {
            const cts = new CancellationTokenSource();
            const pcs = new PromiseCompletionSource<void>();
            cts.cancel();
            cts.token.bind(pcs);
            cts.dispose();
        });

        it(`should reject the ${PromiseCompletionSource.name} synchronously when cancellation had already been requested`, async () => {
            const cts = new CancellationTokenSource();
            try {
                cts.cancel();

                const pcs = new PromiseCompletionSource<void>();

                cts.token.bind(pcs);

                expect(pcs.trySetCanceled()).to.equal(false);

                let actual = undefined;
                try {
                    await pcs.promise;
                } catch (error) {
                    actual = error;
                }

                expect(actual).to.be.instanceOf(OperationCanceledError);
            } finally {
                cts.dispose();
            }
        });

        it(`should not throw when the CT is none`, () => {
            const ct = CancellationToken.none;
            const pcs = new PromiseCompletionSource<void>();

            const act = () => {
                ct.bind(pcs);
            };

            expect(act).not.to.throw();
        });

        it(`should cause the PCS's promise to be rejected right when the CTS is canceled`, async () => {
            const cts = new CancellationTokenSource();
            try {
                const pcs = new PromiseCompletionSource<void>();
                cts.token.bind(pcs);

                await PromisePal.delay(100);
                expect(pcs._internal._reachedFinalState).to.equal(false);

                cts.cancel();
                await PromisePal.delay(10);
                expect(pcs._internal._reachedFinalState).to.equal(true);

                let actual = undefined;
                try {
                    await pcs.promise;
                } catch (error) {
                    actual = error;
                }

                expect(actual).to.be.instanceOf(OperationCanceledError);
            } finally {
                cts.dispose();
            }
        });
    });

    describe(`"${__('register')}" method`, () => {
        let cts: CancellationTokenSource = undefined!;
        let ct: CancellationToken = undefined!;

        beforeEach(() => {
            cts = new CancellationTokenSource();
            ct = cts.token;
        });

        afterEach(() => {
            cts.dispose();
        });

        it(`⚡ should not throw`, () => {
            const act = () => ct.register(() => {});

            expect(act).not.to.throw();
        });

        describe(`⚡ should throw when called with invalid args`, () => {
            const invalidArgs = [123, true, 'some string'] as any[];
            for (const arg of invalidArgs) {
                it(`args === ${JSON.stringify(arg)}`, () => {
                    const act = () => ct.register(arg);

                    expect(act).to.throw();
                });
            }
        });
        it(`⚡ should call the received function inline when the CT had been already canceled`, async () => {
            cts.cancel();

            let invoked = false;

            ct.register(() => {
                invoked = true;
            });

            expect(invoked).to.equal(true);
        });
    });
});
