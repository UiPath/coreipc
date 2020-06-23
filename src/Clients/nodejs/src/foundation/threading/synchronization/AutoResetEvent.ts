import { CancellationToken, PromiseCompletionSource } from '@foundation';

/* @internal */
export class AutoResetEvent {
    private _signalled = false;
    private _pcs: PromiseCompletionSource<void> | null = null;

    constructor(initialState: boolean = false) {
        this._signalled = initialState;
    }

    public set(): void {
        if (this._pcs) {
            this._pcs.setResult();
            this._pcs = null;
        } else {
            this._signalled = true;
        }
    }

    public reset(): void {
        this._signalled = false;
    }

    public waitOne(ct: CancellationToken = CancellationToken.none): Promise<void> {
        if (this._signalled) {
            this._signalled = false;
            return Promise.completedPromise;
        } else {
            return (this._pcs = this._pcs ?? new PromiseCompletionSource<void>()).promise;
        }
    }
}
