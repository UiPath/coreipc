export class ObjectDisposedError extends Error {
    private static readonly defaultMessage = 'Cannot access a disposed object.';
    public static computeMessage(objectName: string, message?: string): string {
        return `${message || this.defaultMessage}\r\nObject name: ${objectName}`;
    }
    constructor(objectName: string, message?: string) { super(ObjectDisposedError.computeMessage(objectName, message)); }
}
