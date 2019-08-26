export class ArgumentError extends Error {
    public static readonly defaultMessage = 'Value does not fall within the expected range.';
    public static computeMessage(message?: string, paramName?: string): string {
        message = message || this.defaultMessage;
        return `${message}${paramName ? `\r\nParameter name: ${paramName}` : ''}`;
    }
    constructor(message?: string, paramName?: string) { super(ArgumentError.computeMessage(message, paramName)); }
}
