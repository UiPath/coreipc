import { assertArgument, AggregateError, ArgumentError, UnknownError } from '..';
import { IDisposable } from '.';

/* @internal */
export class AggregateDisposable implements IDisposable {
    public static maybeCreate(...disposables: IDisposable[]): AggregateDisposable | null {
        if (disposables.length === 0) {
            return null;
        }

        return new AggregateDisposable(...disposables);
    }

    private readonly _disposables: IDisposable[];

    public constructor(...disposables: IDisposable[]) {
        assertArgument({ disposables }, Array);

        if (disposables.length === 0) {
            throw new ArgumentError('No disposables were supplied.');
        }

        if (disposables.filter(x => typeof x['dispose'] !== 'function').length !== 0) {
            throw new ArgumentError('At least one supplied item is not an IDispoable.');
        }

        this._disposables = disposables;
    }

    public dispose(): void {
        const errors = new Array<Error>();
        for (const disposable of this._disposables) {
            try {
                disposable.dispose();
            } catch (error) {
                errors.push(UnknownError.ensureError(error));
            }
        }
        if (errors.length > 0) {
            throw new AggregateError(undefined, ...errors);
        }
    }
}
