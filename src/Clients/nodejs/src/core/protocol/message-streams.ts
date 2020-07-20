// tslint:disable: no-namespace no-internal-module variable-name no-shadowed-variable

import { Observer } from 'rxjs';

import {
    BitConverter,
    CancellationToken,
    IAsyncDisposable,
    Stream,
    CancellationTokenSource,
    OperationCanceledError,
    Trace,
} from '@foundation';

/* @internal */
export interface IMessageStreamFactory {
    create(stream: Stream, observer: Observer<Network.Message>): IMessageStream;
}

/* @internal */
export interface IMessageStream extends IMessageWriter, IAsyncDisposable { }

/* @internal */
export class MessageStreamFactory implements IMessageStreamFactory {
    public static orDefault(factory: IMessageStreamFactory | undefined): IMessageStreamFactory {
        return factory ?? new MessageStreamFactory();
    }

    public create(stream: Stream, observer: Observer<Network.Message>): IMessageStream {
        return new MessageStream(stream, observer);
    }
}

/* @internal */
export class MessageStream implements IMessageStream {
    constructor(stream: Stream, observer: Observer<Network.Message>);
    constructor(
        private readonly _stream: Stream,
        private readonly _observer: Observer<Network.Message>,
    ) { }

    public async writeMessageAsync(message: Network.Message, ct: CancellationToken): Promise<void> {
        await this._stream.write(BitConverter.getBytes(message.type, 'uint8'), ct);
        await this._stream.write(BitConverter.getBytes(message.data.byteLength, 'int32le'), ct);
        await this._stream.write(message.data, ct);
    }
    public async disposeAsync(): Promise<void> {
        this._ctsLoop.cancel();
        await this._loop;

        this._stream.dispose();
    }

    private static async readFully(stream: Stream, length: number, ct: CancellationToken): Promise<Buffer> {
        const buffer = Buffer.alloc(length);
        let soFar = 0;
        while (soFar < length) {
            soFar += await stream.read(buffer, soFar, length - soFar, ct);
        }
        return buffer;
    }

    private async readMessageAsync(ct: CancellationToken): Promise<Network.Message> {
        const type: Network.Message.Type = BitConverter.getNumber(await MessageStream.readFully(this._stream, 1, ct), 'uint8');
        const length = BitConverter.getNumber(await MessageStream.readFully(this._stream, 4, ct), 'int32le');
        const data = await MessageStream.readFully(this._stream, length, ct);

        return { type, data };
    }

    private readonly _ctsLoop = new CancellationTokenSource();
    private get ctLoop(): CancellationToken { return this._ctsLoop.token; }
    private readonly _loop: Promise<void> = this.loop();

    private async loop(): Promise<void> {
        try {
            while (!this.ctLoop.isCancellationRequested) {
                try {
                    const message = await this.readMessageAsync(this.ctLoop);
                    try {
                        this._observer.next(message);
                    } catch (error) {
                        Trace.log(error);
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
export interface IMessageWriter {
    writeMessageAsync(message: Network.Message, ct: CancellationToken): Promise<void>;
}

export module Network {
    export interface Message {
        readonly type: Message.Type;
        readonly data: Buffer;
    }

    export module Message {
        export enum Type {
            Request = 0,
            Response = 1,
            Cancel = 2,
        }
    }
}
