import {
    AsyncAutoResetEvent,
} from '@foundation'

import { nameof } from 'ts-simple-nameof';

for (const args of [
    [],
    [false],
    [true],
]) {
    test(`instantiating ${nameof(AsyncAutoResetEvent)} should not throw`, async () => {
        const act = () => new AsyncAutoResetEvent(...args);

        expect(act).not.toThrow();
    });
}
