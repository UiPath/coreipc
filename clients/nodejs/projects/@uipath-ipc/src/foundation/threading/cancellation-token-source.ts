import { CancellationToken, ProperCancellationToken } from './cancellation-token';
import { IDisposable } from '../disposable';
import { TimeSpan } from './timespan';
import { EcmaTimeout } from './ecma-timeout';
import { ObjectDisposedError, ArgumentError } from '@foundation/errors';

export class CancellationTokenSource implements IDisposable {
    private readonly _token: ProperCancellationToken = new ProperCancellationToken();
    public get token(): CancellationToken { return this._token; }
    private _maybeTimeout: IDisposable | null = null;
    private _isDisposed = false;

    constructor();
    constructor(millisecondsDelay: number);
    constructor(delay: TimeSpan);
    constructor(arg0?: number | TimeSpan) {
        if (arg0 != null) {
            this.cancelAfter(TimeSpan.toTimeSpan(arg0));
        }
    }

    public cancel(throwOnFirstError: boolean = false): void {
        if (this._isDisposed) { throw new ObjectDisposedError('CancellationTokenSource'); }
        this._token.cancel(throwOnFirstError);
    }

    public cancelAfter(millisecondsDelay: number): void;
    public cancelAfter(delay: TimeSpan): void;
    public cancelAfter(arg0: number | TimeSpan): void {
        const delay = TimeSpan.toTimeSpan(arg0);
        if (delay.isNegative) { throw new ArgumentError('Expecting a non-negative delay.', 'arg0'); }

        if (this._isDisposed) { throw new ObjectDisposedError('CancellationTokenSource'); }
        if (!this._token.isCancellationRequested && !this._maybeTimeout) {
            this._maybeTimeout = EcmaTimeout.maybeCreate(delay, this.cancel.bind(this));
        }
    }

    public dispose(): void {
        if (!this._isDisposed) {
            this._isDisposed = true;
            if (this._maybeTimeout) {
                this._maybeTimeout.dispose();
            }
        }
    }
}
