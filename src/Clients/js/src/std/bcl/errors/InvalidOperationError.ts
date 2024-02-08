import { CoreIpcError } from '.';
import { assertArgument } from '../helpers';

export class InvalidOperationError extends CoreIpcError {
    constructor(message?: string) {
        super(InvalidOperationError.computeFullMessage(message));
    }

    private static computeFullMessage(message?: string): string {
        assertArgument({ message }, 'undefined', 'string');

        return message ?? 'Operation is not valid due to the current state of the object.';
    }
}
