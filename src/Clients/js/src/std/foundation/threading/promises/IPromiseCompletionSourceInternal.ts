import {
    FinalState,
} from '@foundation';

/* @internal */
export interface IPromiseCompletionSourceInternal<T = unknown> {
    readonly promise: Promise<T>;

    setFinalState(finalState: FinalState<T>): void | never;
    trySetFinalState(finalState: FinalState<T>): boolean;
}
