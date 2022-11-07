import {
    IDisposable,
} from '@foundation';

import {
    CancellationTokenSource,
} from '.';

/* @internal */
export class LinkedCancellationTokenSource extends CancellationTokenSource {
    constructor(private readonly _registration: IDisposable) {
        super();
    }

    public dispose(): void {
        if (this._isDisposed) { return; }

        super.dispose();
        this._registration.dispose();
    }
}
