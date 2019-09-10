/* @internal */
export interface ISocketLike {
    connect(path: string, connectionListener?: () => void): this;
    once(event: 'error', listener: (err: Error) => void): this;

    write(buffer: Uint8Array | string, cb?: (err?: Error) => void): boolean;

    addListener(event: 'data', listener: (data: Buffer) => void): this;
    addListener(event: 'end', listener: () => void): this;

    removeListener(event: 'data', listener: (data: Buffer) => void): this;
    removeListener(event: 'end', listener: () => void): this;
    removeAllListeners(event?: string | symbol): this;

    unref(): void;
    destroy(error?: Error): void;
}
