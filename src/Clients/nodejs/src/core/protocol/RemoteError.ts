export class RemoteError extends Error {
    constructor(
        public readonly endpoint: string,
        public readonly methodName: string,
        inner: Exception,
    ) {
        super(`${endpoint}.${methodName} threw ${inner.message}.`);
        this.name = inner.type;
        this.inner = inner.inner;
    }

    public inner: Exception | undefined;
}

export interface Exception {
    readonly type: string;
    readonly message: string;
    readonly stackTrace: string;
    readonly inner?: Exception;
}
