import { CancellationToken } from '../threading/cancellation-token';
import { IAsyncDisposable } from '../disposable';
import { ObjectDisposedError } from '../errors/object-disposed-error';
import { PipeReader } from './pipe-reader';
import { ILogicalSocketFactory, ILogicalSocket } from './logical-socket';
import { TimeSpan } from '../threading/timespan';
import { PipeBrokenError } from '../errors/pipe/pipe-broken-error';
import { Trace } from '@foundation/utils';
import { ArgumentNullError } from '@foundation/errors';

/* @internal */
export interface IPipeClientStream {
    writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void>;
    readAsync(destination: Buffer, cancellationToken: CancellationToken): Promise<void>;
    disposeAsync(): Promise<void>;
}

/* @internal */
export class PipeClientStream implements IPipeClientStream, IAsyncDisposable {
    private static readonly _traceWrite = Trace.category('io:write');
    private static readonly _traceRead = Trace.category('io:read');

    public static async connectAsync(
        factory: ILogicalSocketFactory,
        name: string,
        maybeTimeout: TimeSpan | null,
        traceNetwork: boolean,
        cancellationToken: CancellationToken = CancellationToken.none
    ): Promise<PipeClientStream> {
        if (!factory) { throw new ArgumentNullError('factory'); }
        if (!name) { throw new ArgumentNullError('name'); }

        return await this.connectInternalAsync(factory, name, maybeTimeout, traceNetwork, cancellationToken);
    }
    private static async connectInternalAsync(
        factory: ILogicalSocketFactory,
        name: string,
        maybeTimeout: TimeSpan | null,
        traceNetwork: boolean,
        cancellationToken: CancellationToken
    ): Promise<PipeClientStream> {
        const path = `\\\\.\\pipe\\${name}`;
        const socket = factory();

        await socket.connectAsync(path, maybeTimeout, cancellationToken);

        return new PipeClientStream(socket, traceNetwork);
    }

    private readonly _pipeReader: PipeReader;
    private _isDisposed = false;

    private constructor(
        private readonly _socket: ILogicalSocket,
        private readonly _traceNetwork: boolean
    ) {
        this._pipeReader = new PipeReader(_socket);
    }

    public async writeAsync(buffer: Buffer, cancellationToken: CancellationToken = CancellationToken.none): Promise<void> {
        if (!buffer) { throw new ArgumentNullError('buffer'); }
        if (this._isDisposed) { return Promise.fromError(new ObjectDisposedError('PipeClientStream')); }
        if (buffer.length === 0) { return; }

        if (this._traceNetwork) {
            PipeClientStream._traceWrite.log(buffer.toString());
        }

        return await this._socket.writeAsync(buffer, cancellationToken);
    }

    public async readAsync(destination: Buffer, cancellationToken: CancellationToken = CancellationToken.none): Promise<void> {
        if (!destination) { throw new ArgumentNullError('destination'); }

        while (destination.length > 0) {
            const cbRead = await this.readPartiallyAsync(destination, cancellationToken);
            destination = destination.subarray(cbRead);
        }
    }
    public async readPartiallyAsync(destination: Buffer, cancellationToken: CancellationToken = CancellationToken.none): Promise<number> {
        if (!destination) { throw new ArgumentNullError('destination'); }
        if (this._isDisposed) { throw new ObjectDisposedError('PipeClientStream'); }

        const cbRead = await this._pipeReader.readPartiallyAsync(destination, cancellationToken);

        if (this._traceNetwork) {
            PipeClientStream._traceRead.log(destination.subarray(0, cbRead).toString());
        }

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
