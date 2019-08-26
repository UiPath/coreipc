export class PromiseCanceledError extends Error {
    public static readonly defaultMessage = 'A promise was canceled.';
    constructor(message?: string) { super(message || PromiseCanceledError.defaultMessage); }
}
