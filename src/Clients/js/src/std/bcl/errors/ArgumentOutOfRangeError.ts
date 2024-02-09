import { ArgumentErrorBase } from '.';

export class ArgumentOutOfRangeError extends ArgumentErrorBase {
    constructor(paramName?: string, message?: string) {
        super('Specified argument was out of the range of valid values.', message, paramName);
    }
}
