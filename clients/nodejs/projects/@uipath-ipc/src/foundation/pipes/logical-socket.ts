import { IDisposable } from '../disposable';
import { CancellationToken } from '../threading/cancellation-token';
import { TimeSpan } from '../threading/timespan';
import { Observable, Subject } from 'rxjs';
import { ISocketLike } from './socket-like';
import { ILogicalSocket } from './logical-socket';
import { InvalidOperationError } from '../errors/invalid-operation-error';
import { ObjectDisposedError } from '../errors/object-disposed-error';
import { PromiseCompletionSource } from '../threading/promise-completion-source';
import { EcmaTimeout } from '../threading/ecma-timeout';
import { TimeoutError } from '../errors/timeout-error';
import { PipeBrokenError } from '../errors/pipe/pipe-broken-error';
import { ArgumentNullError } from '../errors/argument-null-error';

export type ILogicalSocketFactory = () => ILogicalSocket;

/* ILogicalSocket translates the operations defined by ISocketLike while employing TAP and the Observer Pattern for its methods and events. */
export interface ILogicalSocket extends IDisposable {
    connectAsync(path: string, maybeTimeout: TimeSpan | null, cancellationToken: CancellationToken): Promise<void>;
    writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void>;

    readonly data: Observable<Buffer>;
}

/* This class translates an ISocketLike into an ILogicalSocket. */
export class LogicalSocket implements ILogicalSocket {
    private _isDisposed = false;
    private _mayNotConnect = false;
    private _isConnected = false;

    private readonly _boundDataHandler: any;
    private readonly _boundEndHandler: any;

    private readonly _data = new Subject<Buffer>();
    public get data(): Observable<Buffer> { return this._data; }

    constructor(private readonly _socketLike: ISocketLike) {
        if (!_socketLike) { throw new ArgumentNullError('_socketLike'); }

        this._boundDataHandler = this.onData.bind(this);
        this._boundEndHandler = this.onEnd.bind(this);

        _socketLike.
            addListener('data', this._boundDataHandler).
            addListener('end', this._boundEndHandler);
    }

    private onData(buffer: Buffer): void {
        this._data.next(buffer);
    }
    private onEnd(): void {
        this._data.complete();
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
        this._socketLike.connect(path, () => pcs.trySetResult(undefined));
        this._socketLike.once('error', error => pcs.trySetError(error));
        const ctreg = cancellationToken.register(() => {
            pcs.trySetCanceled();
        });
        const timeout = EcmaTimeout.maybeCreate(maybeTimeout, () => {
            pcs.trySetError(new TimeoutError());
        });
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

            try {
                this._socketLike.removeAllListeners();
                this._socketLike.unref();
                this._socketLike.destroy();
            } catch (error2) {
            }
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
        const ctreg = cancellationToken.register(() => {
            pcs.trySetCanceled();
        });
        this._socketLike.write(buffer, maybeError => {
            if (!maybeError) {
                pcs.trySetResult(undefined);
            } else {
                pcs.trySetError(maybeError);
            }
        });
        try {
            return await pcs.promise;
        } finally {
            ctreg.dispose();
        }
    }
    public dispose(): void {
        if (!this._isDisposed) {
            this._isDisposed = true;
            this._data.complete();
            if (this._isConnected) {
                this._socketLike.removeAllListeners();
                this._socketLike.unref();
                this._socketLike.destroy();
            }
        }
    }
}
