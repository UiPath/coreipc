import { CoreIpcError } from '.';
import { argumentIs } from '../helpers';

export class ObjectDisposedError extends CoreIpcError {
    constructor(
        objectName?: string,
        message?: string,
    ) {
        super(ObjectDisposedError.computeFullMessage(objectName, message));

        this.objectName = objectName ?? null;
    }

    public readonly objectName: string | null;

    private static computeFullMessage(objectName?: string, message?: string): string {
        argumentIs(objectName, 'objectName', 'undefined', 'string');
        argumentIs(message, 'message', 'undefined', 'string');

        message = message ?? 'Cannot access a disposed object.';
        if (objectName) {
            message = `${message}\r\nObject name: '${objectName}'.`;
        }
        return message;
    }
}
