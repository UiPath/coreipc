export class ArgumentNullError extends Error {
    public static readonly defaultMessage = 'Value cannot be null.';

    /* @internal */
    public static computeMessage(maybeParamName?: string, message?: string): string {
        message = message || this.defaultMessage;
        return `${message}${maybeParamName ? `\r\nParameter name: ${maybeParamName}` : ''}`;
    }

    constructor(public readonly maybeParamName?: string, message?: string) { super(ArgumentNullError.computeMessage(maybeParamName, message)); }
}
