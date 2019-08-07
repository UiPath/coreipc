import { IDisposable, EndOfStreamError, Quack, PromiseCompletionSource, CancellationToken } from '@uipath/ipc-helpers';
import { IPipeWrapper, PipeDataSignal, PipeClosedSignal } from './pipe-wrapper';
import { Subscription } from 'rxjs';

/* @internal */
export interface IChannelReader extends IDisposable {
    readBufferAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void>;
}

/* @internal */
export class ChannelReader implements IChannelReader {
    private readonly _subscription: Subscription;
    private readonly _window = new BufferWindow();

    constructor(private readonly _pipe: IPipeWrapper) {
        this._subscription = this._pipe.signals.subscribe(signal => {
            if (signal instanceof PipeDataSignal) {
                this._window.enqueue(signal.data);
                return;
            }

            /* istanbul ignore else */
            if (signal instanceof PipeClosedSignal) {
                this._window.markClosed(signal.maybeError);
            } else {
                throw new Error(`Not supported PipeSignal type (${signal})`);
            }
        });
    }

    public async readBufferAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
        while (buffer.length > 0) {
            const cbRead = await this._window.readAsync(buffer, cancellationToken);
            if (cbRead === 0) {
                throw new EndOfStreamError(this._window.maybeLastError);
            }

            buffer = buffer.slice(cbRead);
        }
    }
    public dispose(): void {
        this._pipe.dispose();
        this._subscription.unsubscribe();
    }
}

// tslint:disable-next-line: max-classes-per-file
class BufferWindow {
    private readonly _buffers = new Quack<Buffer | PoisonPill>();
    private _isReading = false;
    private _pcsDataAvailable: PromiseCompletionSource<void> | null = null;

    private _maybeLastError: Error | null = null;
    public get maybeLastError(): Error | null { return this._maybeLastError; }

    private waitForDataAsync(cancellationToken: CancellationToken): Promise<void> {
        const pcsDataAvailable = new PromiseCompletionSource<void>();
        this._pcsDataAvailable = pcsDataAvailable;

        cancellationToken.register(() => {
            pcsDataAvailable.trySetCanceled();
        });
        return pcsDataAvailable.promise;
    }

    // tslint:disable-next-line: member-ordering
    public enqueue(buffer: Buffer): void {
        this._buffers.enqueue(buffer);
        if (this._pcsDataAvailable) {
            this._pcsDataAvailable.trySetResult(undefined);
            this._pcsDataAvailable = null;
        }
    }
    // tslint:disable-next-line: member-ordering
    public markClosed(maybeError: Error | null) {
        this._buffers.enqueue(new PoisonPill(maybeError));
        if (this._pcsDataAvailable) {
            this._pcsDataAvailable.trySetResult(undefined);
            this._pcsDataAvailable = null;
        }
    }

    // This method must not be called concurrently
    // (the method along with its declaring class are internal)
    // tslint:disable-next-line: member-ordering
    public async readAsync(destination: Buffer, cancellationToken: CancellationToken): Promise<number> {
        if (this._isReading) {
            throw new Error('The method BufferWindow.readAsync must not be called concurrently');
        }

        /* istanbul ignore next */
        if (destination.length === 0) { return 0; }

        try {
            this._isReading = true;

            if (this._buffers.empty) { await this.waitForDataAsync(cancellationToken); }

            let cbRead = 0;
            while (destination.length > 0 && this._buffers.any) {
                const token = this._buffers.dequeue();

                /* instanbul ignore else */
                if (token instanceof PoisonPill) {
                    this._maybeLastError = token.getError();
                    return 0;
                }

                /* istanbul ignore else */
                if (token instanceof Buffer) {
                    const cbAdvance = Math.min(destination.length, token.length);
                    token.copy(destination, 0, 0, cbAdvance);
                    cbRead += cbAdvance;

                    const remainder = token.slice(cbAdvance);
                    destination = destination.slice(cbAdvance);

                    if (remainder.length > 0) {
                        this._buffers.pushFront(remainder);
                    }
                } else /* istanbul ignore next */ {
                    throw new Error(`Unexpected token (${token})`);
                }
            }

            return cbRead;
        } finally {
            this._isReading = false;
        }
    }
}

// tslint:disable-next-line: max-classes-per-file
class PoisonPill {
    constructor(private readonly _maybeError: Error | null) { }

    public getError(): Error { return new EndOfStreamError(this._maybeError); }
}
