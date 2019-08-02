export interface IAsyncDisposable {
    disposeAsync(): Promise<void>;
}
