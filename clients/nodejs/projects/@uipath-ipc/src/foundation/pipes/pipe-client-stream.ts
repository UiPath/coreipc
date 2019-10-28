import { CancellationToken } from '../threading/cancellation-token';
import { IAsyncDisposable } from '../disposable';
import { ObjectDisposedError } from '../errors/object-disposed-error';
import { PipeReader } from './pipe-reader';
import { ILogicalSocketFactory, ILogicalSocket } from './logical-socket';
import { TimeSpan } from '../threading/timespan';
import { PipeBrokenError } from '../errors/pipe/pipe-broken-error';
import { Trace } from '../../foundation/utils';
import { ArgumentNullError } from '../../foundation/errors';

export interface IPipeClientStream extends IAsyncDisposable {
    writeAsync(source: Buffer, cancellationToken: CancellationToken): Promise<void>;
    readAsync(destination: Buffer, cancellationToken: CancellationToken): Promise<void>;
}

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
    private _maybeDisposeTask: Promise<void> | null = null;
    private get isDisposedOrDisposing(): boolean { return !!this._maybeDisposeTask; }

    private constructor(
        private readonly _socket: ILogicalSocket,
        private readonly _traceNetwork: boolean
    ) {
        this._pipeReader = new PipeReader(_socket);
    }

    public async writeAsync(source: Buffer, cancellationToken: CancellationToken = CancellationToken.none): Promise<void> {
        if (!source) { throw new ArgumentNullError('source'); }
        if (this.isDisposedOrDisposing) { return Promise.fromError(new ObjectDisposedError('PipeClientStream')); }
        if (source.length === 0) { return; }

        if (this._traceNetwork) {
            PipeClientStream._traceWrite.log(source.toString());
        }

        return await this._socket.writeAsync(source, cancellationToken);
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
        if (this.isDisposedOrDisposing) { throw new ObjectDisposedError('PipeClientStream'); }

        const cbRead = await this._pipeReader.readPartiallyAsync(destination, cancellationToken);

        if (this._traceNetwork) {
            PipeClientStream._traceRead.log(destination.subarray(0, cbRead).toString());
        }

        if (0 === cbRead) {
            throw new PipeBrokenError();
        }
        return cbRead;
    }

    public disposeAsync(): Promise<void> {
        return this._maybeDisposeTask || (this._maybeDisposeTask = this.disposeCoreAsync().observe());
    }
    private async disposeCoreAsync(): Promise<void> {
        this._socket.dispose();
        await this._pipeReader.disposeAsync();
    }
}
