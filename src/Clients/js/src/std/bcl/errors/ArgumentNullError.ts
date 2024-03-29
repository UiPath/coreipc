import { ArgumentErrorBase } from '.';

export class ArgumentNullError extends ArgumentErrorBase {
    constructor(paramName?: string, message?: string) {
        super('Value cannot be null.', message, paramName);
    }
}
