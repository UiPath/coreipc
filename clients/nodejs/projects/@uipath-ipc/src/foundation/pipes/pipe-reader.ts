import * as net from 'net';
import { Quack } from '../data-structures/quack';
import { InvalidOperationError } from '../errors/invalid-operation-error';
import { IAsyncDisposable } from '../disposable/disposable';
import { ObjectDisposedError } from '../errors/object-disposed-error';
import { PromiseCompletionSource } from '../tasks/promise-completion-source';
import { CancellationTokenSource } from '../tasks/cancellation-token-source';
import { CancellationToken } from '../tasks/cancellation-token';

/* @internal */
export class PipeReader implements IAsyncDisposable {
    private readonly _buffers = new Quack<Buffer>();

    private readonly _boundHandler: (data: Buffer) => void;
    private _isDisposed = false;
    private _currentRead: Promise<number> | null = null;
    private _cts = new CancellationTokenSource();
    private _maybeDataAvailableSignal: PromiseCompletionSource<void> | null = null;

    constructor(private readonly _socket: net.Socket) {
        this._boundHandler = this.onData.bind(this);
        _socket.addListener('data', this._boundHandler);
    }

    private onData(data: Buffer): void {
        this._buffers.enqueue(data);
        if (this._maybeDataAvailableSignal) { this._maybeDataAvailableSignal.setResult(undefined); }
    }
    public readPartiallyAsync(destination: Buffer, cancellationToken: CancellationToken): Promise<number> {
        if (this._isDisposed) { throw new ObjectDisposedError('PipeReader'); }
        if (this._currentRead) { throw new InvalidOperationError('Cannot read twice concurrently.'); }

        this._currentRead = this.readInternalAsync(destination, cancellationToken);
        this._currentRead.then(
            _ => { this._currentRead = null; },
            _ => { this._currentRead = null; }
        );
        return this._currentRead;
    }
    private async readInternalAsync(destination: Buffer, cancellationToken: CancellationToken): Promise<number> {
        if (this._buffers.empty && destination.length > 0) {
            const pcsDataAvailable = new PromiseCompletionSource<void>();
            const ctReg = cancellationToken.registerIfCanBeCanceled(pcsDataAvailable.setCanceled.bind(pcsDataAvailable));

            this._maybeDataAvailableSignal = pcsDataAvailable;
            await pcsDataAvailable.promise;
            ctReg.dispose();
        }

        let result = 0;
        while (this._buffers.any && destination.length > 0) {
            const source = this._buffers.pop();
            const progress = Math.min(source.length, destination.length);
            source.copy(destination, 0, 0, progress);
            result += progress;
            destination = destination.subarray(progress);
            if (progress < source.length) {
                const remainder = source.subarray(progress);
                this._buffers.push(remainder);
            }
        }
        return result;
    }

    public async disposeAsync(): Promise<void> {
        if (!this._isDisposed) {
            // this._socket.removeListener('data', this._boundHandler);
            // this._cts.cancel();`);

            this._isDisposed = true;
            this._socket.removeListener('data', this._boundHandler);
            this._cts.cancel();

            if (this._currentRead) {
                try {
                    // TODO: Find out why we can't wait for this
                    // await this._currentRead;
                } catch (error) {
                    /* */
                }
            }
        }
    }
}
