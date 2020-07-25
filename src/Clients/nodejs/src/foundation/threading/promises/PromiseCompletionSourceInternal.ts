import { argumentIs, InvalidOperationError, OperationCanceledError } from '../../../foundation';
import {
    FinalState,
    FinalStateRanToCompletion,
    FinalStateFaulted,
    FinalStateCanceled,
} from '.';
import { TimeoutError } from '@foundation-errors';

/* @internal */
export interface IPromiseCompletionSourceInternal<T = unknown> {
    readonly promise: Promise<T>;

    setFinalState(finalState: FinalState<T>): void | never;
    trySetFinalState(finalState: FinalState<T>): boolean;
}

/* @internal */
export class PromiseCompletionSourceInternal<T = unknown> implements IPromiseCompletionSourceInternal<T> {
    public static create<T>(): IPromiseCompletionSourceInternal<T> { return new PromiseCompletionSourceInternal<T>(); }

    private _reachedFinalState = false;
    private readonly _completer: Completer<T> = null as never;

    constructor() {
        const _this = this as unknown as CompleterContainer<T>;
        const assignCompleter = (resolve: Resolver<T>, reject: Rejecter) => {
            _this._completer = new Completer(resolve, reject);
        };

        this.promise = new Promise<T>(assignCompleter).observe();
    }

    public readonly promise: Promise<T>;

    public setFinalState(finalState: FinalState<T>): void | never {
        argumentIs(finalState, 'finalState', FinalStateRanToCompletion, FinalStateFaulted, FinalStateCanceled);

        if (!this.trySetFinalStateUnchecked(finalState)) {
            throw new InvalidOperationError('An attempt was made to transition a task to a final state when it had already completed.');
        }
    }

    public trySetFinalState(finalState: FinalState<T>): boolean {
        argumentIs(finalState, 'finalState', FinalStateRanToCompletion, FinalStateFaulted, FinalStateCanceled);

        return this.trySetFinalStateUnchecked(finalState);
    }

    private trySetFinalStateUnchecked(finalState: FinalState<T>): boolean {
        if (!this._reachedFinalState) {
            this._reachedFinalState = true;
            this._completer.setFinalState(finalState);
            return true;
        }

        return false;
    }
}

type Resolver<T> = (result: T) => void;
type Rejecter = (error: Error) => void;

class Completer<T> {
    constructor(
        private readonly _resolve: Resolver<T>,
        private readonly _reject: Rejecter,
    ) { }

    public setFinalState(finalState: FinalState<T>): void {
        if (finalState.isRanToCompletion()) {
            this._resolve(finalState.result);
        } else if (finalState.isFaulted()) {
            this._reject(finalState.error);
        } else {
            this._reject(new OperationCanceledError());
        }
    }
}

interface CompleterContainer<T> { _completer: Completer<T>; }
