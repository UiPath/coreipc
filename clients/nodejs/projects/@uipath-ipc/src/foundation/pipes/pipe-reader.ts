import { Quack } from '../utils/quack';
import { InvalidOperationError } from '../errors/invalid-operation-error';
import { IAsyncDisposable, IDisposable } from '../disposable';
import { ObjectDisposedError } from '../errors/object-disposed-error';
import { PromiseCompletionSource } from '../threading/promise-completion-source';
import { CancellationTokenSource } from '../threading/cancellation-token-source';
import { CancellationToken } from '../threading/cancellation-token';
import { ILogicalSocket } from './logical-socket';
import { ArgumentNullError } from '../errors/argument-null-error';
import { Subscription } from 'rxjs';

/* @internal */
export class PipeReader implements IAsyncDisposable {
    private readonly _buffers = new Quack<Buffer | null>();

    private _isDisposed = false;
    private _currentRead: Promise<number> | null = null;
    private _cts = new CancellationTokenSource();
    private _maybeDataAvailableSignal: PromiseCompletionSource<void> | null = null;
    private _subscription: Subscription;

    constructor(socket: ILogicalSocket) {
        if (!socket) { throw new ArgumentNullError('socket'); }
        this._subscription = socket.data.subscribe({
            next: this.onNext.bind(this),
            complete: this.onComplete.bind(this)
        });
    }

    private onNext(data: Buffer): void {
        this._buffers.enqueue(data);
        if (this._maybeDataAvailableSignal) { this._maybeDataAvailableSignal.setResult(undefined); }
    }
    private onComplete(): void {
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
            this._subscription.unsubscribe();
            this._cts.cancel();
        }
    }
}
