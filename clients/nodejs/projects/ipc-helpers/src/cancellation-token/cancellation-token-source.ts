import { CancellationToken, SimpleCancellationToken } from './cancellation-token';
import { Action0 } from '../delegates/delegates';
import { IDisposable, Disposable } from '../disposable/disposable';
import { AggregateError } from '../exceptions/aggregate-error';
import { Lazy } from '../helpers/lazy';

export class CancellationTokenSource {

    private static readonly _default = new Lazy<CancellationTokenSource>(() => new DefaultCancellationTokenSource());
    public static get default(): CancellationTokenSource { return CancellationTokenSource._default.value; }

    /* @internal */
    public isCancellationRequested = false;

    public readonly token: CancellationToken = new SimpleCancellationToken(this);

    private readonly _handlers = new Array<Action0>();
    private _maybeTimeout: NodeJS.Timer | null = null;

    public cancel(throwOnFirstError: boolean = false): void {
        if (this.isCancellationRequested) {
            return;
        }
        if (this._maybeTimeout) {
            clearTimeout(this._maybeTimeout);
        }

        this.isCancellationRequested = true;

        const errors = new Array<Error>();
        for (const handler of this._handlers) {
            try {
                handler();
            } catch (error) {
                if (throwOnFirstError) {
                    throw error;
                } else {
                    errors.push(error);
                }
            }
        }
        this._handlers.splice(0);
        if (errors.length > 0) {
            throw new AggregateError(errors);
        }
    }
    public cancelAfter(milliseconds: number): void {
        if (this.isCancellationRequested) {
            return;
        }
        if (this._maybeTimeout) {
            clearTimeout(this._maybeTimeout);
        }
        this._maybeTimeout = setTimeout(
            () => this.cancel(),
            milliseconds);
    }
    /* @internal */
    public register(handler: Action0): IDisposable {
        if (this.isCancellationRequested) {
            handler();
            return Disposable.empty;
        } else {
            this._handlers.push(handler);
            return new Disposable(() => this.unregister(handler));
        }
    }
    private unregister(handler: Action0): void {
        const index = this._handlers.indexOf(handler);
        if (index >= 0) {
            this._handlers.splice(index, 1);
        }
    }
}

// tslint:disable-next-line: max-classes-per-file
class DefaultCancellationTokenSource extends CancellationTokenSource {
    public register(handler: Action0): IDisposable { return Disposable.empty; }
    // tslint:disable-next-line: no-empty
    public cancel(throwOnFirstError: boolean = false) { }
    // tslint:disable-next-line: no-empty
    public cancelAfter(milliseconds: number): void { }
}
