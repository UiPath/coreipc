/* istanbul ignore file */

export class ArgumentNullError extends Error {
    public static readonly defaultMessage = 'Value cannot be null.';

    /* @internal */
    public static computeMessage(paramName?: string, message?: string): string {
        message = message || this.defaultMessage;
        return `${message}${paramName ? `\r\nParameter name: ${paramName}` : ''}`;
    }

    constructor(public readonly paramName?: string, message?: string) {
        super(ArgumentNullError.computeMessage(paramName, message));
        this.name = 'ArgumentNullError';
    }
}
