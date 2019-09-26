import { IDisposable } from '../disposable';
import { InvalidOperationError } from '../errors/invalid-operation-error';
import { ObjectDisposedError } from '../errors/object-disposed-error';
import { CancellationToken } from '../tasks/cancellation-token';
import { PromiseCompletionSource } from '../tasks/promise-completion-source';
import { EcmaTimeout } from '../tasks/ecma-timeout';
import { TimeSpan } from '../tasks/timespan';
import { TimeoutError } from '../errors/timeout-error';
import { PipeBrokenError } from '../errors/pipe/pipe-broken-error';
import { ISocketLike } from './socket-like';
import { ILogicalSocket } from './logical-socket';
import { ArgumentNullError } from '../errors/argument-null-error';
import * as net from 'net';

/* @internal */
export class SocketAdapter implements ILogicalSocket {
    private _isDisposed = false;
    private _mayNotConnect = false;
    private _isConnected = false;

    constructor(private readonly _socketLike: ISocketLike) {
        if (!_socketLike) { throw new ArgumentNullError('_socketLike'); }
    }

    public async connectAsync(path: string, maybeTimeout: TimeSpan | null, cancellationToken: CancellationToken): Promise<void> {
        if (!path) { throw new ArgumentNullError('path'); }
        if (!cancellationToken) { throw new ArgumentNullError('cancellationToken'); }

        if (this._isDisposed) {
            throw new ObjectDisposedError('NamedPipe');
        }
        if (this._mayNotConnect) {
            throw new InvalidOperationError();
        }
        this._mayNotConnect = true;
        const pcs = new PromiseCompletionSource<void>();
        const ctreg = cancellationToken.register(() => {
            pcs.trySetCanceled();
        });
        const timeout = EcmaTimeout.maybeCreate(maybeTimeout, () => {
            pcs.trySetError(new TimeoutError());
        });
        this._socketLike.connect(path, () => pcs.trySetResult(undefined));
        this._socketLike.once('error', error => pcs.trySetError(error));
        try {
            try {
                await pcs.promise;
                this._isConnected = true;
            } catch (error) {
                if (error instanceof Error && (error as NodeJS.ErrnoException).code === 'EPIPE') {
                    error = new PipeBrokenError();
                }
                throw error;
            }
        } finally {
            ctreg.dispose();
            timeout.dispose();
        }
    }
    public async writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
        if (!buffer) { throw new ArgumentNullError('buffer'); }
        if (!cancellationToken) { throw new ArgumentNullError('cancellationToken'); }

        if (this._isDisposed) {
            throw new ObjectDisposedError('PhysicalSocket');
        }
        if (!this._isConnected) {
            throw new InvalidOperationError();
        }
        const pcs = new PromiseCompletionSource<void>();
        const ctreg = cancellationToken.register(() => pcs.setCanceled());
        this._socketLike.write(buffer, maybeError => {
            if (!maybeError) {
                pcs.setResult(undefined);
            } else {
                pcs.setError(maybeError);
            }
        });
        try {
            return await pcs.promise;
        } finally {
            ctreg.dispose();
        }
    }
    public addDataListener(listener: (data: Buffer) => void): IDisposable {
        if (!listener) { throw new ArgumentNullError('listener'); }

        if (this._isDisposed) {
            throw new ObjectDisposedError('PhysicalSocket');
        }
        if (!this._isConnected) {
            throw new InvalidOperationError();
        }
        this._socketLike.addListener('data', listener);
        return {
            dispose: () => this._socketLike.removeListener('data', listener)
        };
    }
    public addEndListener(listener: () => void): IDisposable {
        if (!listener) { throw new ArgumentNullError('listener'); }

        if (this._isDisposed) {
            throw new ObjectDisposedError('PhysicalSocket');
        }
        if (!this._isConnected) {
            throw new InvalidOperationError();
        }
        this._socketLike.addListener('end', listener);
        return {
            dispose: () => this._socketLike.removeListener('end', listener)
        };
    }
    public dispose(): void {
        if (!this._isDisposed) {
            this._isDisposed = true;
            if (this._isConnected) {
                this._socketLike.removeAllListeners();
                this._socketLike.unref();
                this._socketLike.destroy();
            }
        }
    }
}
