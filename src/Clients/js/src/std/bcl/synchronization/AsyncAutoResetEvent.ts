import {
    assertArgument,
    PromiseCompletionSource,
    CancellationToken,
    CancellationTokenRegistration,
} from '../..';

export class AsyncAutoResetEvent {
    private _signalled = false;
    private _awaiters = new Array<Awaiter>();

    constructor(initialState: boolean = false) {
        assertArgument({ initialState }, 'boolean');
        this._signalled = initialState;
    }

    public set(): void {
        const _ = this._awaiters.pop()?.signal() ?? (this._signalled = true);
    }

    public async waitOne(ct: CancellationToken = CancellationToken.none): Promise<void> {
        if (this._signalled) {
            this._signalled = false;

            return;
        }

        const awaiter = new Awaiter(ct);
        this._awaiters.splice(0, 0, awaiter);

        return awaiter.promise;
    }
}

class Awaiter {
    constructor(ct: CancellationToken) {
        if (ct.canBeCanceled) {
            this._ctreg = ct.register(this.cancel);
        }
    }

    public get promise(): Promise<void> {
        return this._pcs.promise;
    }

    public signal(): Awaiter {
        this._pcs.trySetResult();
        if (this._ctreg) {
            this._ctreg.dispose();
        }
        return this;
    }

    private cancel = () => this._pcs.trySetCanceled();

    private readonly _pcs = new PromiseCompletionSource<void>();

    private readonly _ctreg: CancellationTokenRegistration | undefined;
}
