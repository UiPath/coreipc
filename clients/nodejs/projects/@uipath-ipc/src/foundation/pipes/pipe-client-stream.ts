import * as net from 'net';
import { PromiseCompletionSource } from '../tasks/promise-completion-source';
import { CancellationToken } from '../tasks/cancellation-token';
import { IAsyncDisposable } from '../disposable/disposable';
import { TimeoutError } from '../errors/timeout-error';
import { ObjectDisposedError } from '../errors/object-disposed-error';
import { Timeout } from '../tasks/timeout';
import { IDisposable } from '../disposable/disposable';
import { Result, Faulted, Canceled, Succeeded } from '../result/result';
import { PipeReader } from './pipe-reader';
import { PipeBrokenError } from '../errors/pipe/pipe-broken-error';

export class PipeClientStream implements IAsyncDisposable {
    public static async connectAsync(
        name: string,
        maybeTimeoutMilliseconds: number | null,
        cancellationToken: CancellationToken = CancellationToken.none
    ): Promise<PipeClientStream> {
        return await this.connectInternalAsync(name, maybeTimeoutMilliseconds, cancellationToken);
    }
    private static maybeCreateTimeout(maybeMilliseconds: number | null, callback: () => void): IDisposable {
        if (maybeMilliseconds) {
            return new Timeout(maybeMilliseconds, callback);
        } else {
            return { dispose: () => { /* */ } };
        }
    }
    private static async connectInternalAsync(
        name: string,
        timeoutMilliseconds: number | null,
        cancellationToken: CancellationToken
    ): Promise<PipeClientStream> {
        const path = `\\\\.\\pipe\\${name}`;
        const pcs = new PromiseCompletionSource<void>();

        const socket = new net.Socket({ allowHalfOpen: true });

        const timeout = this.maybeCreateTimeout(timeoutMilliseconds, () => {
            applyResult(new Faulted(new TimeoutError()));
        });
        const ctReg = cancellationToken.registerIfCanBeCanceled(() => {
            applyResult(Canceled.instance);
        });
        const applyResult = (result: Result<void>) => {
            if (!result.isSucceeded) {
                socket.unref();
                socket.destroy();
            } else {
                socket.removeAllListeners();
            }

            timeout.dispose();
            ctReg.dispose();

            pcs.trySet(result);
        };

        socket.once('error', error => applyResult(new Faulted(error)));
        socket.connect(path, () => applyResult(new Succeeded<void>(undefined)));

        await pcs.promise;
        return new PipeClientStream(socket);
    }

    private readonly _pipeReader: PipeReader;
    private _isDisposed = false;

    private constructor(private readonly _socket: net.Socket) {
        this._pipeReader = new PipeReader(_socket);
    }

    public async writeAsync(buffer: Buffer, cancellationToken: CancellationToken = CancellationToken.none): Promise<void> {
        if (this._isDisposed) { throw new ObjectDisposedError('PipeClientStream'); }
        if (buffer.length === 0) { return; }

        const pcs = new PromiseCompletionSource<void>();
        const ctReg = cancellationToken.registerIfCanBeCanceled(() => applyResult(Canceled.instance));
        const applyResult = (result: Result<void>) => {
            if (result instanceof Faulted) {
                let finalError = (result.error as NodeJS.ErrnoException);
                if (finalError.code === 'EPIPE') {
                    finalError = new PipeBrokenError();
                }
                result = new Faulted(finalError);
            }
            ctReg.dispose();
            pcs.trySet(result);
        };
        try {
            this._socket.write(buffer, maybeError => {
                if (maybeError) {
                    applyResult(new Faulted(maybeError));
                } else {
                    applyResult(new Succeeded<void>(undefined));
                }
            });
        } catch (error) {
            applyResult(new Faulted(error));
        }

        await pcs.promise;
    }

    public async readAsync(destination: Buffer, cancellationToken: CancellationToken = CancellationToken.none): Promise<void> {
        while (destination.length > 0) {
            const cbRead = await this.readPartiallyAsync(destination, cancellationToken);
            destination = destination.subarray(cbRead);
        }
    }
    public async readPartiallyAsync(destination: Buffer, cancellationToken: CancellationToken = CancellationToken.none): Promise<number> {
        if (this._isDisposed) { throw new ObjectDisposedError('PipeClientStream'); }
        const cbRead = await this._pipeReader.readPartiallyAsync(destination, cancellationToken);
        return cbRead;
    }

    public async disposeAsync(): Promise<void> {

        if (!this._isDisposed) {
            this._isDisposed = true;
            this._socket.removeAllListeners();
            this._socket.unref();
            try {
                this._socket.destroy();
            } catch (error) { }
            await this._pipeReader.disposeAsync();
        }
    }
}
