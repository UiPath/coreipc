import { argumentIs } from '../../../foundation';
import { FinalState, IPromiseCompletionSource } from '.';

import { IPromiseCompletionSourceInternal } from './IPromiseCompletionSourceInternal';
import { PromiseCompletionSourceInternal } from './PromiseCompletionSourceInternal';

export class PromiseCompletionSource<T = unknown> implements IPromiseCompletionSource<T> {
    private readonly _internal: IPromiseCompletionSourceInternal<T>;

    public constructor(internal?: any) {
        argumentIs(internal, 'internal', 'undefined', 'object');

        this._internal = internal ?? new PromiseCompletionSourceInternal<T>();
    }

    public get promise(): Promise<T> { return this._internal.promise; }

    public setFinalState(finalState: FinalState<T>): void {
        this._internal.setFinalState(finalState);
    }
    public trySetFinalState(finalState: FinalState<T>): boolean {
        return this._internal.trySetFinalState(finalState);
    }

    public setResult(result: T): void {
        this.setFinalState(FinalState.ranToCompletion(result));
    }
    public setFaulted(error: Error): void {
        this.setFinalState(FinalState.faulted(error));
    }
    public setCanceled(): void {
        this.setFinalState(FinalState.canceled());
    }

    public trySetResult(result: T): boolean {
        return this.trySetFinalState(FinalState.ranToCompletion(result));
    }
    public trySetFaulted(error: Error): boolean {
        return this.trySetFinalState(FinalState.faulted(error));
    }
    public trySetCanceled(): boolean {
        return this.trySetFinalState(FinalState.canceled());
    }
}
