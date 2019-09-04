import { OperationCanceledError } from '../errors/operation-canceled-error';
import { PromiseCompletionSource } from '../..';

export type Any<T> = Succeeded<T> | Faulted<T> | Canceled<T>;
export enum OutcomeKind {
    Succeeded,
    Faulted,
    Canceled
}

export abstract class Base<T> {
    public isSucceeded(): this is Succeeded<T> { return false; }
    public isFaulted(): this is Faulted<T> { return false; }
    public isCanceled(): this is Canceled<T> { return false; }

    public abstract get kind(): OutcomeKind;
    public abstract get result(): T;

    public abstract apply(pcs: PromiseCompletionSource<T>): void;
    public abstract tryApply(pcs: PromiseCompletionSource<T>): boolean;
}

export class Succeeded<T> extends Base<T> {
    public isSucceeded(): this is Succeeded<T> { return true; }
    public readonly kind = OutcomeKind.Succeeded;

    constructor(public readonly result: T) { super(); }

    public apply(pcs: PromiseCompletionSource<T>): void { pcs.setResult(this.result); }
    public tryApply(pcs: PromiseCompletionSource<T>): boolean { return pcs.trySetResult(this.result); }
}
export class Faulted<T> extends Base<T> {
    public isFaulted(): this is Faulted<T> { return true; }
    public readonly kind = OutcomeKind.Faulted;
    public get result(): T { throw this.error; }

    constructor(public readonly error: Error) { super(); }

    public apply(pcs: PromiseCompletionSource<T>): void { pcs.setError(this.error); }
    public tryApply(pcs: PromiseCompletionSource<T>): boolean { return pcs.trySetError(this.error); }
}
export class Canceled<T> extends Base<T> {
    public isCanceled(): this is Canceled<T> { return true; }
    public readonly kind = OutcomeKind.Canceled;
    public get result(): T { throw new OperationCanceledError(); }

    constructor() { super(); }

    public apply(pcs: PromiseCompletionSource<T>): void { pcs.setCanceled(); }
    public tryApply(pcs: PromiseCompletionSource<T>): boolean { return pcs.trySetCanceled(); }
}
