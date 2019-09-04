import { CancellationToken, ProperCancellationToken } from './cancellation-token';
import { IDisposable } from '../disposable/disposable';
import { TimeSpan } from './timespan';
import { Timeout } from './timeout';
import { ObjectDisposedError } from '../errors/object-disposed-error';

export class CancellationTokenSource implements IDisposable {
    private readonly _token: ProperCancellationToken = new ProperCancellationToken();
    public get token(): CancellationToken { return this._token; }
    private _maybeTimeout: Timeout | null = null;
    private _isDisposed = false;

    public cancel(throwOnFirstError: boolean = false): void {
        if (this._isDisposed) { throw new ObjectDisposedError('CancellationTokenSource'); }
        this._token.cancel(throwOnFirstError);
    }
    public cancelAfter(timeSpan: TimeSpan): void {
        if (this._isDisposed) { throw new ObjectDisposedError('CancellationTokenSource'); }
        if (!this._token.isCancellationRequested && !this._maybeTimeout) {
            this._maybeTimeout = new Timeout(timeSpan, this.cancel.bind(this));
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
