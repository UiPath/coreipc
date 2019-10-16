/* istanbul ignore file */

export class ArgumentError extends Error {
    public static readonly defaultMessage = 'Value does not fall within the expected range.';

    /* @internal */
    public static computeMessage(message?: string, maybeParamName?: string): string {
        message = message || this.defaultMessage;
        return `${message}${maybeParamName ? `\r\nParameter name: ${maybeParamName}` : ''}`;
    }

    constructor(message?: string, public readonly maybeParamName?: string) { super(ArgumentError.computeMessage(message, maybeParamName)); }
}
