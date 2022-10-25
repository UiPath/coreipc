export interface IPromiseCompletionSource<T = unknown> {
    setResult(result: T): void | never;
    setFaulted(error: Error): void | never;
    setCanceled(): void | never;

    trySetResult(result: T): boolean;
    trySetFaulted(error: Error): boolean;
    trySetCanceled(): boolean;
}
