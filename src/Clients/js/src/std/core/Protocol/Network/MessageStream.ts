import { Observer } from 'rxjs';

import {
    CancellationToken,
    CancellationTokenSource,
    Stream,
    UnknownError,
    OperationCanceledError,
    BitConverter,
    Trace,
} from '../../../bcl';

import { IMessageStream, Network } from '.';

/* @internal */
export class MessageStream implements IMessageStream {
    private static readonly _trace = Trace.category('network');

    constructor(stream: Stream, observer: Observer<Network.Message>);
    constructor(
        private readonly _stream: Stream,
        private readonly _observer: Observer<Network.Message>,
    ) {}

    public async writeMessageAsync(message: Network.Message, ct: CancellationToken): Promise<void> {
        const bytes = Buffer.from([
            ...BitConverter.getBytes(message.type, 'uint8'),
            ...BitConverter.getBytes(message.data.byteLength, 'int32le'),
            ...message.data,
        ]);
        MessageStream._trace.log(
            `Writing ${MessageStream.toMessageTypeString(
                message.type,
            )} with data: ${message.data.toString()}`,
        );
        await this._stream.write(bytes, ct);
    }
    public async disposeAsync(): Promise<void> {
        this._ctsLoop.cancel();
        await this._loop;

        this._stream.dispose();
    }

    private static toMessageTypeString(messageType: Network.Message.Type): string {
        switch (messageType) {
            case Network.Message.Type.Request:
                return 'Request';
            case Network.Message.Type.Response:
                return 'Response';
            case Network.Message.Type.Cancel:
                return 'Cancel';
            default:
                return 'Unknown';
        }
    }

    private static async readFully(
        stream: Stream,
        length: number,
        ct: CancellationToken,
    ): Promise<Buffer> {
        const buffer = Buffer.alloc(length);
        let soFar = 0;
        while (soFar < length) {
            soFar += await stream.read(buffer, soFar, length - soFar, ct);
        }
        return buffer;
    }

    private async readMessageAsync(ct: CancellationToken): Promise<Network.Message> {
        const type: Network.Message.Type = BitConverter.getNumber(
            await MessageStream.readFully(this._stream, 1, ct),
            'uint8',
        );
        const length = BitConverter.getNumber(
            await MessageStream.readFully(this._stream, 4, ct),
            'int32le',
        );
        const data = await MessageStream.readFully(this._stream, length, ct);

        return { type, data };
    }

    private readonly _ctsLoop = new CancellationTokenSource();
    private get ctLoop(): CancellationToken {
        return this._ctsLoop.token;
    }
    private readonly _loop: Promise<void> = this.loop();

    private async loop(): Promise<void> {
        try {
            while (!this.ctLoop.isCancellationRequested) {
                try {
                    const message = await this.readMessageAsync(this.ctLoop);
                    try {
                        this._observer.next(message);
                    } catch (error) {
                        Trace.log(UnknownError.ensureError(error));
                    }
                } catch (error) {
                    if (!(error instanceof OperationCanceledError)) {
                        this._observer.error(error);
                    }
                    throw error;
                }
            }
        } finally {
            this._observer.complete();
        }
    }
}

/* @internal */
export module MessageStream {
    export class Factory implements IMessageStream.Factory {
        public static orDefault(
            factory: IMessageStream.Factory | undefined,
        ): IMessageStream.Factory {
            return factory ?? new Factory();
        }

        public create(stream: Stream, observer: Observer<Network.Message>): IMessageStream {
            return new MessageStream(stream, observer);
        }
    }
}
