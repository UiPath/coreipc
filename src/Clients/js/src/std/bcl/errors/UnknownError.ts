import { CoreIpcError } from '.';

export class UnknownError extends CoreIpcError {
    public static ensureError(error: any): Error {
        if (error instanceof Error) {
            return error;
        }

        return new UnknownError(error);
    }

    constructor(public readonly inner: any) {
        super();
    }
}
