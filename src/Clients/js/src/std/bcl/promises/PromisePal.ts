function emptyOnFulfilled<T>(result: T): T {
    return result;
}
function emptyOnRejected(reason: any) {}

/* @internal */
export class PromisePal {
    public static ensureObserved<T>(promise: Promise<T>): Promise<T> {
        return promise.then(emptyOnFulfilled, emptyOnRejected) as any;
    }
}
