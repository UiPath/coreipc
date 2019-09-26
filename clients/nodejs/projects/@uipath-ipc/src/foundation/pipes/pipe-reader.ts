import { Quack } from '../data-structures/quack';
import { InvalidOperationError } from '../errors/invalid-operation-error';
import { IAsyncDisposable, IDisposable } from '../disposable';
import { ObjectDisposedError } from '../errors/object-disposed-error';
import { PromiseCompletionSource } from '../tasks/promise-completion-source';
import { CancellationTokenSource } from '../tasks/cancellation-token-source';
import { CancellationToken } from '../tasks/cancellation-token';
import { ILogicalSocket } from './logical-socket';
import { ArgumentNullError } from '../errors/argument-null-error';

/* @internal */
export class PipeReader implements IAsyncDisposable {
    private readonly _buffers = new Quack<Buffer | null>();

    private _isDisposed = false;
    private _currentRead: Promise<number> | null = null;
    private _cts = new CancellationTokenSource();
    private _maybeDataAvailableSignal: PromiseCompletionSource<void> | null = null;
    private _dataListenerSubscription: IDisposable;
    private _endListenerSubscription: IDisposable;

    constructor(private readonly _socket: ILogicalSocket) {
        if (!_socket) { throw new ArgumentNullError('_socket'); }
        this._dataListenerSubscription = _socket.addDataListener(this.onData.bind(this));
        this._endListenerSubscription = _socket.addEndListener(this.onEnd.bind(this));
    }

    private onData(data: Buffer): void {
        this._buffers.enqueue(data);
        if (this._maybeDataAvailableSignal) { this._maybeDataAvailableSignal.setResult(undefined); }
    }
    private onEnd(): void {
        this._buffers.enqueue(null);
        if (this._maybeDataAvailableSignal) { this._maybeDataAvailableSignal.setResult(undefined); }
    }
    public async readPartiallyAsync(destination: Buffer, cancellationToken: CancellationToken): Promise<number> {
        if (!destination) { throw new ArgumentNullError('destination'); }
        if (!cancellationToken) { throw new ArgumentNullError('cancellationToken'); }

        if (this._isDisposed) { throw new ObjectDisposedError('PipeReader'); }
        if (this._currentRead) { throw new InvalidOperationError('Cannot read twice concurrently.'); }

        const currentRead = this.readInternalAsync(destination, cancellationToken);
        this._currentRead = currentRead;
        try {
            return await currentRead;
        } finally {
            this._currentRead = null;
        }
    }
    private async readInternalAsync(destination: Buffer, cancellationToken: CancellationToken): Promise<number> {
        if (this._buffers.empty && destination.length > 0) {
            const pcsDataAvailable = new PromiseCompletionSource<void>();
            const ctReg = cancellationToken.register(pcsDataAvailable.setCanceled.bind(pcsDataAvailable));

            this._maybeDataAvailableSignal = pcsDataAvailable;
            await pcsDataAvailable.promise;
            ctReg.dispose();
        }

        let result = 0;
        while (this._buffers.any && destination.length > 0) {
            const source = this._buffers.pop();
            if (source) {
                const progress = Math.min(source.length, destination.length);
                source.copy(destination, 0, 0, progress);
                result += progress;
                destination = destination.subarray(progress);
                if (progress < source.length) {
                    const remainder = source.subarray(progress);
                    this._buffers.push(remainder);
                }
            }
        }

        return result;
    }

    public async disposeAsync(): Promise<void> {
        if (!this._isDisposed) {
            this._isDisposed = true;
            this._dataListenerSubscription.dispose();
            this._endListenerSubscription.dispose();
            this._cts.cancel();
        }
    }
}
