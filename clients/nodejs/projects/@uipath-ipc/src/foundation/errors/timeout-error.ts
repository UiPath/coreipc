export class TimeoutError extends Error {
    public static readonly defaultMessage = 'The operation has timed out.';
    constructor(message?: string) { super(message || TimeoutError.defaultMessage); }
}
