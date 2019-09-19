export class RemoteError extends Error {
    private static readonly _defaultMessage = 'A remote method responded with an error.';

    /* @internal */
    public static computeMessage(receivedError: Error, methodName?: string, message?: string) {
        const head = methodName
            ? `${message || this._defaultMessage}\r\n\tMethod name: ${methodName}`
            : message || this._defaultMessage;
        return `${head}\r\n\tReceived Error: ${receivedError.name}\r\n\tReceived Error Message: ${receivedError.message}\r\n\tReceived Error Stack: ${receivedError.stack}`;
    }

    constructor(public readonly receivedError: Error, methodName?: string, message?: string) {
        super(RemoteError.computeMessage(receivedError, methodName, message));
    }
}
