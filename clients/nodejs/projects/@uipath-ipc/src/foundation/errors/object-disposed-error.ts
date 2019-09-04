export class ObjectDisposedError extends Error {
    private static readonly defaultMessage = 'Cannot access a disposed object.';

    /* @internal */
    public static computeMessage(objectName: string, message?: string): string {
        return `${message || this.defaultMessage}\r\nObject name: ${objectName}`;
    }

    constructor(public readonly objectName: string, message?: string) { super(ObjectDisposedError.computeMessage(objectName, message)); }
}
