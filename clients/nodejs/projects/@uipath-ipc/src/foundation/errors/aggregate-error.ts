export class AggregateError extends Error {
    public static readonly defaultMessage = 'One or more errors occurred.';
    public readonly errors: ReadonlyArray<Error>;

    constructor(...errors: Error[]);
    constructor(message: string, ...errors: Error[]);
    constructor(maybeMessageOrError: string | Error | undefined, ...errors: Error[]) {
        let _message: string | null = null;
        const _errors = [...errors];

        if (typeof maybeMessageOrError === 'string') {
            _message = maybeMessageOrError;
        } else if (typeof maybeMessageOrError === 'object' && maybeMessageOrError instanceof Error) {
            _errors.splice(0, 0, maybeMessageOrError);
        }

        super(_message || AggregateError.defaultMessage);
        this.errors = _errors;
    }
}
