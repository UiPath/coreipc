import { ArgumentNullError } from './argument-null-error';

export class AggregateError extends Error {

    private static assert(errors: Error[]): Error[] {
        if (errors == null) {
            throw new ArgumentNullError('errors');
        }
        if (errors.length === 0) {
            throw new Error('Expecting a non-empty error list');
        }
        return errors;
    }

    /* istanbul ignore next */
    constructor(public readonly errors: Error[]) {
        super(`One or more errors occurred: ${AggregateError.concatenate(AggregateError.assert(errors))}`);
    }

    // tslint:disable-next-line: member-ordering
    private static concatenate(errors: Error[]): string {
        let result = '';
        let isFirst = true;
        for (const error of errors) {
            if (isFirst) {
                result = error.message;
                isFirst = false;
            } else {
                result += ', ' + error.message;
            }
        }
        return result;
    }
}
