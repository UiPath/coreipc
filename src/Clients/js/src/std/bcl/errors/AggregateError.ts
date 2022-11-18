import { assertArgument } from '..';
import { CoreIpcError, ArgumentOutOfRangeError } from '.';

export class AggregateError extends CoreIpcError {
    public static maybeAggregate(...errors: Error[]): Error | undefined {
        switch (errors.length) {
            case 0:
                return undefined;
            case 1:
                return errors[0];
            default:
                return new AggregateError(undefined, ...errors);
        }
    }

    constructor(message?: string, ...errors: Error[]) {
        super(AggregateError.computeFullMessage(message, ...errors));
        this.errors = errors;
    }

    public readonly errors: Error[];

    private static computeFullMessage(message?: string, ...errors: Error[]): string {
        assertArgument({ message }, 'undefined', 'string');

        if (errors.filter((x) => !(x instanceof Error)).length > 0) {
            throw new ArgumentOutOfRangeError(
                'errors',
                `Specified argument contained at least one element which is not an Error.`,
            );
        }

        message = message ?? 'One or more errors occurred.';
        if (errors.length > 0) {
            const strErrors = errors.map((error) => `(${error.message})`).join(' ');
            message = `${message} ${strErrors}`;
        }
        return message;
    }
}
