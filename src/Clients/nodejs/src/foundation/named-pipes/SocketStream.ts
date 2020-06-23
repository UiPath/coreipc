import { argumentIs, CancellationToken, AutoResetEvent, ArgumentError, EndOfStreamError, InvalidOperationError } from '@foundation';
import { Stream, Socket } from '.';

/* @internal */
export class SocketStream implements Stream {
    private readonly _signal = new AutoResetEvent();
    private _buffers = new Array<Buffer>();
    private _oldestBufferCursor = 0;
    private _completed = false;
    private _reading = false;

    constructor(socket: Socket);
    constructor(private readonly _socket: Socket) {
        argumentIs(_socket, 'socket', Socket);

        _socket.$data.subscribe(
            this.handleData,
            undefined,
            this.handleCompletion);
    }

    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> {
        argumentIs(buffer, 'buffer', Buffer);
        argumentIs(ct, 'ct', CancellationToken);

        if (buffer.byteLength === 0) { return; }
        await this._socket.write(buffer, ct);
    }

    public async read(buffer: Buffer, offset: number, length: number, ct: CancellationToken): Promise<number> {
        if (this._reading) {
            throw new InvalidOperationError('An asynchronous read operation is already in progress.');
        }

        try {
            this._reading = true;

            argumentIs(buffer, 'buffer', Buffer);
            argumentIs(ct, 'ct', CancellationToken);
            if (offset + length > buffer.byteLength) {
                throw new ArgumentError(
                    'Offset and length overflow outside the destination buffer.',
                );
            }

            if (length === 0) { return 0; }

            let cbRead = Math.min(this.cbAvailable, length);
            if (cbRead === 0 && !this._completed) {
                await this._signal.waitOne(ct);
                cbRead = Math.min(this.cbAvailable, length);
            }

            if (cbRead === 0 && this._completed) {
                throw new EndOfStreamError();
            }

            let cbRemaining = cbRead;
            while (cbRemaining > 0) {
                const oldestBuffer = this._buffers[0];
                const cbRemainingInOldestBuffer = oldestBuffer.byteLength - this._oldestBufferCursor;

                const cbCopied = oldestBuffer.copy(
                    buffer,
                    offset,
                    this._oldestBufferCursor,
                    this._oldestBufferCursor + Math.max(cbRemaining, cbRemainingInOldestBuffer),
                );
                if (cbRemainingInOldestBuffer <= cbRemaining) {
                    this._buffers.splice(0, 1);
                    this._oldestBufferCursor = 0;
                } else {
                    this._oldestBufferCursor += cbCopied;
                }

                cbRemaining -= cbCopied;
                offset += cbCopied;
            }

            return cbRead;
        } finally {
            this._reading = false;
        }
    }

    public dispose(): void {
        this._socket.dispose();
    }

    private get cbAvailable(): number {
        return this._buffers.reduce((sum, buffer) => sum + buffer.byteLength, -this._oldestBufferCursor);
    }

    private handleData = (buffer: Buffer): void => {
        this._buffers.push(buffer);
        this._signal.set();
    }

    private handleCompletion = (): void => {
        this._completed = true;
        this._signal.set();
    }
}
