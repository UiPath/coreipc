import { CancellationToken } from '../tasks/cancellation-token';
import { IAsyncDisposable } from '../disposable/disposable';
import { ObjectDisposedError } from '../errors/object-disposed-error';
import { PipeReader } from './pipe-reader';
import { ILogicalSocketFactory, ILogicalSocket } from './logical-socket';
import { TimeSpan } from '../tasks/timespan';
import { PromisePal } from '../..';
import { PipeBrokenError } from '../errors/pipe/pipe-broken-error';

export class PipeClientStream implements IAsyncDisposable {
    public static async connectAsync(
        factory: ILogicalSocketFactory,
        name: string,
        maybeTimeout: TimeSpan | null,
        cancellationToken: CancellationToken = CancellationToken.none
    ): Promise<PipeClientStream> {
        return await this.connectInternalAsync(factory, name, maybeTimeout, cancellationToken);
    }
    private static async connectInternalAsync(
        factory: ILogicalSocketFactory,
        name: string,
        maybeTimeout: TimeSpan | null,
        cancellationToken: CancellationToken
    ): Promise<PipeClientStream> {
        const path = `\\\\.\\pipe\\${name}`;
        const socket = factory();

        await socket.connectAsync(path, maybeTimeout, cancellationToken);

        return new PipeClientStream(socket);
    }

    private readonly _pipeReader: PipeReader;
    private _isDisposed = false;

    private constructor(private readonly _socket: ILogicalSocket) {
        this._pipeReader = new PipeReader(_socket);
    }

    public writeAsync(buffer: Buffer, cancellationToken: CancellationToken = CancellationToken.none): Promise<void> {
        if (this._isDisposed) { return PromisePal.fromError(new ObjectDisposedError('PipeClientStream')); }
        if (buffer.length === 0) { return PromisePal.completedPromise; }

        return this._socket.writeAsync(buffer, cancellationToken);
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
        if (0 === cbRead) {
            throw new PipeBrokenError();
        }
        return cbRead;
    }

    public async disposeAsync(): Promise<void> {
        if (!this._isDisposed) {
            this._isDisposed = true;
            this._socket.dispose();
            await this._pipeReader.disposeAsync();
        }
    }
}
