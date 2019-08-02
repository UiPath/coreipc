export class PromiseCompletionSource<T> {
    public readonly promise: Promise<T>;
    private _isCompleted = false;

    constructor() {
        this.promise = new Promise((resolve, reject) => {
            (this as any).resolve = resolve;
            (this as any).reject = reject;
        });
    }

    public trySetResult(value: T): boolean {
        if (this._isCompleted) {
            return false;
        }

        this._isCompleted = true;
        (this as any).resolve(value);
        return true;
    }
    public trySetException(exception: Error): boolean {
        if (this._isCompleted) {
            return false;
        }

        this._isCompleted = true;
        (this as any).reject(exception);
        return true;
    }
    public trySetCanceled(): boolean {
        return this.trySetException(new Error('Task was canceled'));
    }

    public setResult(value: T): void {
        if (!this.trySetResult(value)) {
            throw new Error('An attempt was made to transition a promise to a final state when it had already completed.');
        }
    }
    public setException(exception: Error): void {
        if (!this.trySetException(exception)) {
            throw new Error('An attempt was made to transition a promise to a final state when it had already completed.');
        }
    }
    public setCanceled(): void {
        if (!this.trySetCanceled()) {
            throw new Error('An attempt was made to transition a promise to a final state when it had already completed.');
        }
    }
}
