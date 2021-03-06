import {
    CancellationToken,
    CancellationTokenSource,
} from '.';

import {
    IDisposable,
    argumentIs,
    OperationCanceledError,
} from '../../../foundation';

/* @internal */
export class RandomCancellationToken extends CancellationToken {
    public constructor(private readonly _source: CancellationTokenSource) {
        super();
    }

    public get canBeCanceled(): boolean { return true; }

    public get isCancellationRequested(): boolean { return this._source.isCancellationRequested; }

    public throwIfCancellationRequested(): void {
        if (this._source.isCancellationRequested) {
            throw new OperationCanceledError();
        }
    }

    public register(callback: () => void): IDisposable {
        argumentIs(callback, 'callback', 'function');

        return this._source.registerUnchecked(callback);
    }

    public static toString() { return 'new CancellationToken()'; }
}
