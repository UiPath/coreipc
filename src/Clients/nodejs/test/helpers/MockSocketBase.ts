import { SocketLike, TimeSpan } from '@foundation';

export class MockSocketBase implements SocketLike {
    public once(_event: 'error', _listener: (error: Error) => void): this {
        return this;
    }

    public on(event: 'end', listener: () => void): this;
    public on(event: 'data', listener: (data: Buffer) => void): this;
    public on(_event: string, _listener: (...args: any[]) => void): this {
        return this;
    }

    public connect(_path: string, _connectionListener?: () => void): this {
        return this;
    }
    public write(_buffer: string | Uint8Array, _cb?: (err?: Error) => void): boolean {
        return false;
    }
    public removeAllListeners(_event?: string | symbol): this {
        return this;
    }
    public unref(): void {
    }
    public destroy(_error?: Error): void {
    }
}

export class DelayConnectMockSocket extends MockSocketBase {
    private _timeout: NodeJS.Timeout | null = null;

    constructor(private readonly _connectDelay: TimeSpan) { super(); }

    public connect(path: string, connectionListener?: () => void): this {
        if (connectionListener && !this._connectDelay.isInfinite) {
            this._timeout = setTimeout(connectionListener, this._connectDelay.totalMilliseconds);
        }
        return this;
    }

    public destroy(error?: Error): void {
        if (this._timeout) { clearTimeout(this._timeout); }
    }
}
