import { argumentIs, AggregateError, ArgumentError } from '../../foundation';

export interface IDisposable {
    dispose(): void;
}

export interface IAsyncDisposable {
    disposeAsync(): Promise<void>;
}

/* @internal */
export class AggregateDisposable implements IDisposable {
    private readonly _disposables: IDisposable[];

    public constructor(...disposables: IDisposable[]) {
        argumentIs(disposables, 'disposables', Array);
        if (disposables.length === 0) {
            throw new ArgumentError('No disposables were supplied.');
        }

        this._disposables = disposables;
    }

    public dispose(): void {
        const errors = new Array<Error>();
        for (const disposable of this._disposables) {
            try {
                disposable.dispose();
            } catch (error) {
                errors.push(error);
            }
        }
        if (errors.length > 0) {
            throw new AggregateError(undefined, ...errors);
        }
    }
}
