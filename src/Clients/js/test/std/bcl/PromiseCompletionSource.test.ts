import { PromiseCompletionSource, PromisePal } from '../../../src/std';
import { expect } from 'chai';

describe(`${PromiseCompletionSource.name}`, function () {
    it('should work', async () => {
        const pcs = new PromiseCompletionSource<number>();
        const act = async () => {
            let failed;
            try {
                await pcs.promise;
                failed = false;
            } catch (x) {
                failed = true;
                throw x;
            }
        };

        const error = new Error('Some Message');

        async function parallel() {
            await PromisePal.delay(1);
            pcs.setFaulted(error);
        }

        parallel();

        let caught: any = undefined;

        try {
            await act();
        } catch (error) {
            caught = error;
        }

        expect(caught).to.equal(error);
    });
});
