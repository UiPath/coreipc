import { CancellationToken, CancellationTokenSource } from '.';

import { IDisposable, assertArgument, OperationCanceledError } from '..';

/* @internal */
export class RandomCancellationToken extends CancellationToken {
    public constructor(private readonly _source: CancellationTokenSource) {
        super();
    }

    public get canBeCanceled(): boolean {
        return true;
    }

    public get isCancellationRequested(): boolean {
        return this._source.isCancellationRequested;
    }

    public throwIfCancellationRequested(): void {
        if (this._source.isCancellationRequested) {
            throw new OperationCanceledError();
        }
    }

    public register(callback: () => void): IDisposable {
        assertArgument({ callback }, 'function');

        return this._source.registerUnchecked(callback);
    }

    public static toString() {
        return 'new CancellationToken()';
    }
}
