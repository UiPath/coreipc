import { expect } from 'chai';
import { AsyncAutoResetEvent, PromisePal, TimeSpan } from '../../src/std';

describe(`web:AsyncAutoResetEvent`, function () {
    this.timeout(60 * 1000);

    it(`should work`, async () => {
        const x = new AsyncAutoResetEvent();

        let finished = false;
        const waiter = async () => {
            await x.waitOne();
            finished = true;
        };
        const wait = waiter();

        const lease = PromisePal.delay(TimeSpan.fromMilliseconds(700));
        await Promise.race([wait, lease]);
        expect(finished).to.equal(false);

        x.set();
        await PromisePal.delay(TimeSpan.fromMilliseconds(100));
        expect(finished).to.equal(true);
    });
});
