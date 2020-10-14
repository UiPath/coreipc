import { ArgumentErrorBase } from '.';

export class ArgumentError extends ArgumentErrorBase {
    constructor(
        message?: string,
        public readonly paramName?: string,
    ) {
        super(
            'Value does not fall within the expected range.',
            message,
            paramName,
        );
    }
}
