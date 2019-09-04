import { OperationCanceledError } from '../errors/operation-canceled-error';
import { InvalidOperationError } from '../errors/invalid-operation-error';
import * as Outcome from '../outcome';

export class PromiseCompletionSource<T> {
    public readonly promise: Promise<T>;

    private _isCompleted = false;
    private readonly _resolve: (result: T) => void = null as any;
    private readonly _reject: (error: Error) => void = null as any;

    constructor() {
        this.promise = new Promise<T>((resolve, reject) => {
            const me = this as any;
            me._resolve = resolve;
            me._reject = reject;
        });
        this.promise.then(_ => { }, _ => { });
    }

    public trySet(outcome: Outcome.Any<T>): boolean { return outcome.tryApply(this); }
    public set(outcome: Outcome.Any<T>): void { outcome.apply(this); }

    public trySetResult(result: T): boolean {
        if (this._isCompleted) { return false; }
        this._isCompleted = true;
        this._resolve(result);
        return true;
    }
    public trySetError(error: Error): boolean {
        if (this._isCompleted) { return false; }
        this._isCompleted = true;
        this._reject(error);
        return true;
    }
    public trySetCanceled(): boolean {
        if (this._isCompleted) { return false; }
        this._isCompleted = true;
        this._reject(new OperationCanceledError());
        return true;
    }

    /* @internal */
    public static readonly _errorMessage = 'An attempt was made to transition a task to a final state when it had already completed.';
    public setResult(result: T): void {
        if (!this.trySetResult(result)) {
            throw new InvalidOperationError(PromiseCompletionSource._errorMessage);
        }
    }
    public setError(error: Error): void {
        if (!this.trySetError(error)) {
            throw new InvalidOperationError(PromiseCompletionSource._errorMessage);
        }
    }
    public setCanceled(): void {
        if (!this.trySetCanceled()) {
            throw new InvalidOperationError(PromiseCompletionSource._errorMessage);
        }
    }
}
