export abstract class FinalStateBase<T = unknown> {
    public abstract get kind(): FinalStateKind;

    public isRanToCompletion(): this is FinalStateRanToCompletion<T> {
        return this.kind === FinalStateKind.RanToCompletion;
    }

    public isFaulted(): this is FinalStateFaulted<T> {
        return this.kind === FinalStateKind.Faulted;
    }

    public isCanceled(): this is FinalStateCanceled<T> {
        return this.kind === FinalStateKind.Canceled;
    }
}

export type FinalState<T> =
    | FinalStateRanToCompletion<T>
    | FinalStateFaulted<T>
    | FinalStateCanceled<T>;

// tslint:disable-next-line: no-namespace no-internal-module
export module FinalState {
    export function ranToCompletion<T>(result: T): FinalStateBase<T> {
        return new FinalStateRanToCompletion(result);
    }

    export function faulted<T>(error: Error): FinalStateBase<T> {
        return new FinalStateFaulted<T>(error);
    }

    export function canceled<T>(): FinalStateBase<T> {
        return new FinalStateCanceled<T>();
    }
}

export class FinalStateRanToCompletion<T = unknown> extends FinalStateBase<T> {
    public constructor(public readonly result: T) {
        super();
    }

    public get kind(): FinalStateKind {
        return FinalStateKind.RanToCompletion;
    }
}

export class FinalStateFaulted<T = unknown> extends FinalStateBase<T> {
    public constructor(public readonly error: Error) {
        super();
    }

    public get kind(): FinalStateKind {
        return FinalStateKind.Faulted;
    }
}

export class FinalStateCanceled<T = unknown> extends FinalStateBase<T> {
    public get kind(): FinalStateKind {
        return FinalStateKind.Canceled;
    }
}

export enum FinalStateKind {
    RanToCompletion,
    Faulted,
    Canceled,
}
