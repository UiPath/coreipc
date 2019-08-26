import { CancellationTokenRegistration } from './cancellation-token-registration';
import { PromiseCanceledError } from '../errors/promise-canceled-error';
import { AggregateError } from '../errors/aggregate-error';

export class CancellationToken {
    public static get none(): CancellationToken { return NoneCancellationToken.instance; }

    private readonly _callbacks = new Array<() => void>();
    private _isCancellationRequested = false;

    public get canBeCanceled(): boolean { return true; }
    public get isCancellationRequested(): boolean { return this._isCancellationRequested; }

    /* @internal */
    constructor() { /* */ }

    /* @internal */
    public cancel(throwOnFirstError: boolean): void {
        this._isCancellationRequested = true;
        try {
            if (throwOnFirstError) {
                for (const callback of this._callbacks) {
                    callback();
                }
            } else {
                const errors = new Array<Error>();
                for (const callback of this._callbacks) {
                    try {
                        callback();
                    } catch (error) {
                        errors.push(error);
                    }
                }
                if (errors.length > 0) {
                    throw new AggregateError(...errors);
                }
            }
        } finally {
            this._callbacks.splice(0);
        }
    }

    public register(callback: () => void): CancellationTokenRegistration {
        this._callbacks.push(callback);
        return CancellationTokenRegistration.create(this, callback);
    }
    public registerIfCanBeCanceled(callback: () => void): CancellationTokenRegistration {
        if (this.canBeCanceled) {
            return this.register(callback);
        } else {
            return CancellationTokenRegistration.none;
        }
    }
    /* @internal */
    public unregister(callback: () => void): void {
        const index = this._callbacks.indexOf(callback);
        if (index >= 0) {
            this._callbacks.splice(index, 1);
        }
    }
    public throwIfCancellationRequested(): void {
        if (this._isCancellationRequested) {
            throw new PromiseCanceledError();
        }
    }
}

class NoneCancellationToken extends CancellationToken {
    public static readonly instance = new NoneCancellationToken();

    public get canBeCanceled(): boolean { return false; }
    public get isCancellationRequested(): boolean { return false; }

    private constructor() { super(); }

    public register(callback: () => void): CancellationTokenRegistration { return CancellationTokenRegistration.none; }
    public throwIfCancellationRequested(): void { /* */ }
}
