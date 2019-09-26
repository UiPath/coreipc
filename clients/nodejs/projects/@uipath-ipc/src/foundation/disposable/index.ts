export interface IDisposable {
    dispose(): void;
}
export interface IAsyncDisposable {
    disposeAsync(): Promise<void>;
}
