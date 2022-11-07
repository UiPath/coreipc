import {
    CancellationTokenSource,
} from '@foundation'

import { nameof } from 'ts-simple-nameof';

test(`instantiating ${nameof(CancellationTokenSource)} should not throw`, async () => {
    const act = () => new CancellationTokenSource();

    expect(act).not.toThrow();
});
