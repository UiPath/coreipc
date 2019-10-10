import { OperationCanceledError, ArgumentNullError } from '@foundation/errors';
import { PromiseCompletionSource } from '@foundation/threading';

export type AnyOutcome<T> = Succeeded<T> | Faulted<T> | Canceled<T>;
export enum OutcomeKind {
    Succeeded,
    Faulted,
    Canceled
}

export abstract class OutcomeBase<T> {
    public isSucceeded(): this is Succeeded<T> { return false; }
    public isFaulted(): this is Faulted<T> { return false; }
    public isCanceled(): this is Canceled<T> { return false; }

    public abstract get kind(): OutcomeKind;
    public abstract get result(): T;

    public abstract apply(pcs: PromiseCompletionSource<T>): void;
    public abstract tryApply(pcs: PromiseCompletionSource<T>): boolean;
}

export class Succeeded<T> extends OutcomeBase<T> {
    public isSucceeded(): this is Succeeded<T> { return true; }
    public readonly kind = OutcomeKind.Succeeded;

    constructor(public readonly result: T) { super(); }

    public apply(pcs: PromiseCompletionSource<T>): void {
        if (!pcs) { throw new ArgumentNullError('pcs'); }
        pcs.setResult(this.result);
    }
    public tryApply(pcs: PromiseCompletionSource<T>): boolean {
        if (!pcs) { throw new ArgumentNullError('pcs'); }
        return pcs.trySetResult(this.result);
    }
}
export class Faulted<T> extends OutcomeBase<T> {
    public isFaulted(): this is Faulted<T> { return true; }
    public readonly kind = OutcomeKind.Faulted;
    public get result(): T { throw this.error; }

    constructor(public readonly error: Error) { super(); }

    public apply(pcs: PromiseCompletionSource<T>): void {
        if (!pcs) { throw new ArgumentNullError('pcs'); }
        pcs.setError(this.error);
    }
    public tryApply(pcs: PromiseCompletionSource<T>): boolean {
        if (!pcs) { throw new ArgumentNullError('pcs'); }
        return pcs.trySetError(this.error);
    }
}
export class Canceled<T> extends OutcomeBase<T> {
    public isCanceled(): this is Canceled<T> { return true; }
    public readonly kind = OutcomeKind.Canceled;
    public get result(): T { throw new OperationCanceledError(); }

    constructor() { super(); }

    public apply(pcs: PromiseCompletionSource<T>): void {
        if (!pcs) { throw new ArgumentNullError('pcs'); }
        pcs.setCanceled();
    }
    public tryApply(pcs: PromiseCompletionSource<T>): boolean {
        if (!pcs) { throw new ArgumentNullError('pcs'); }
        return pcs.trySetCanceled();
    }
}
