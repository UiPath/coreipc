import { expect } from 'chai';

import { AmbientCallOptions, TimeSpan } from '../../../src/std';
import { callOptions, withRequestTimeout } from '../../../src/node';

describe('ambient call options', () => {
    it('are absent outside any scope', () => {
        expect(AmbientCallOptions.current()).to.equal(undefined);
    });

    it('are visible inside the scope and restored after it', () => {
        const timeout = TimeSpan.fromSeconds(7);

        withRequestTimeout(timeout, () => {
            expect(AmbientCallOptions.current()?.requestTimeout).to.equal(timeout);
        });

        expect(AmbientCallOptions.current()).to.equal(undefined);
    });

    it('nest, with the inner scope winning for its own duration', () => {
        const outer = TimeSpan.fromSeconds(7);
        const inner = TimeSpan.fromSeconds(3);

        withRequestTimeout(outer, () => {
            withRequestTimeout(inner, () => {
                expect(AmbientCallOptions.current()?.requestTimeout).to.equal(inner);
            });
            expect(AmbientCallOptions.current()?.requestTimeout).to.equal(outer);
        });
    });

    it('flow across asynchronous continuations', async () => {
        const timeout = TimeSpan.fromSeconds(5);

        await callOptions({ requestTimeout: timeout }, async () => {
            await Promise.resolve();
            expect(AmbientCallOptions.current()?.requestTimeout).to.equal(timeout);
        });

        expect(AmbientCallOptions.current()).to.equal(undefined);
    });

    it('are restored when the callback throws', () => {
        expect(() =>
            withRequestTimeout(TimeSpan.fromSeconds(5), () => {
                throw new Error('boom');
            }),
        ).to.throw('boom');

        expect(AmbientCallOptions.current()).to.equal(undefined);
    });
});
