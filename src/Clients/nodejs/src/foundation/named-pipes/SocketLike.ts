/* @internal */
export interface SocketLike {
    once(event: 'error', listener: (error: Error) => void): this;
    on(event: 'end', listener: () => void): this;
    on(event: 'data', listener: (data: Buffer) => void): this;

    connect(path: string, connectionListener?: () => void): this;
    write(buffer: Uint8Array | string, cb?: (err?: Error) => void): boolean;

    removeAllListeners(event?: string | symbol): this;
    unref(): void;
    destroy(error?: Error): void;
}
