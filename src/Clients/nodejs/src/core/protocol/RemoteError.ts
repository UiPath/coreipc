export class RemoteError extends Error {
    constructor(
        public readonly endpoint: string,
        public readonly methodName: string,
        public readonly exception: Exception,
    ) {
        super(`A call to ${endpoint}.${methodName} threw ${exception.type}. Message: "${exception.message}".`);
        this.name = 'RemoteError';
    }
}

export interface Exception {
    readonly type: string;
    readonly message: string;
    readonly stackTrace: string;
    readonly innerException?: Exception;
}
