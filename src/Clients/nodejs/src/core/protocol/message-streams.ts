// tslint:disable: no-namespace no-internal-module variable-name no-shadowed-variable

import { Observable, Subject } from 'rxjs';

import {
    BitConverter,
    CancellationToken,
    IDisposable,
    IAsyncDisposable,
    Stream,
    CancellationTokenSource,
} from '@foundation';

/* @internal */
export interface MessageStream extends IDisposable {
    write(message: Network.Message, ct: CancellationToken): Promise<void>;
    read(ct: CancellationToken): Promise<Network.Message>;
}

/* @internal */
export interface MessageEmitter extends IAsyncDisposable {
    readonly $incommingMessage: Observable<Network.Message>;
}

/* @internal */
export module MessageEmitter {
    export class Impl implements MessageEmitter {
        public static create(messageStream: MessageStream): Impl {
            return new Impl(messageStream);
        }

        private constructor(private readonly _messageStream: MessageStream) {
            this._loop = this.loop();
        }

        public get $incommingMessage(): Observable<Network.Message> { return this._$message; }

        public async disposeAsync(): Promise<void> {
            try {
                await this._loop;
            } catch (error) {
            }
        }

        private readonly _cts = new CancellationTokenSource();
        private readonly _loop: Promise<void>;
        private readonly _$message = new Subject<Network.Message>();

        private async loop(): Promise<void> {
            try {
                while (!this._cts.token.isCancellationRequested) {
                    const message = await this._messageStream.read(this._cts.token);
                    this._$message.next(message);
                }
            } finally {
                this._$message.complete();
            }
        }
    }
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

    export class Impl implements MessageStream {
        constructor(private readonly _stream: Stream) { }

        public async write(message: Message, ct: CancellationToken): Promise<void> {
            await this._stream.write(BitConverter.getBytes(message.type, 'uint8'), ct);
            await this._stream.write(BitConverter.getBytes(message.data.byteLength, 'int32le'), ct);
            await this._stream.write(message.data, ct);
        }

        public async read(ct: CancellationToken): Promise<Message> {
            const type: Network.Message.Type = BitConverter.getNumber(await Impl.readFully(this._stream, 1, ct), 'uint8');
            const length = BitConverter.getNumber(await Impl.readFully(this._stream, 4, ct), 'int32le');
            const data = await Impl.readFully(this._stream, length, ct);

            return { type, data };
        }

        public dispose(): void { this._stream.dispose(); }

        private static async readFully(stream: Stream, length: number, ct: CancellationToken): Promise<Buffer> {
            const buffer = Buffer.alloc(length);
            let soFar = 0;
            while (soFar < length) {
                soFar += await stream.read(buffer, soFar, length - soFar, ct);
            }
            return buffer;
        }
    }
}
