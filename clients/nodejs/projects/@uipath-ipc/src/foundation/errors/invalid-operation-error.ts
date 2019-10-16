/* istanbul ignore file */

export class InvalidOperationError extends Error {
    public static readonly defaultMessage = 'Operation is not valid due to the current state of the object.';
    constructor(message?: string) { super(message || InvalidOperationError.defaultMessage); }
}
