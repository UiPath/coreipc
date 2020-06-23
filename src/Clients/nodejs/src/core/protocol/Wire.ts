// tslint:disable: no-namespace no-internal-module
import { CancellationToken, BitConverter, Stream, IDisposable } from '@foundation';

/* @internal */
export interface Wire extends IDisposable {
    write(message: Wire.Message, ct: CancellationToken): Promise<void>;
    read(ct: CancellationToken): Promise<Wire.Message>;
}

/* @internal */
export module Wire {
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

    export class Default implements Wire {
        constructor(private readonly _stream: Stream) { }

        public async write(message: Message, ct: CancellationToken): Promise<void> {
            await this._stream.write(BitConverter.getBytes(message.type, 'uint8'), ct);
            await this._stream.write(BitConverter.getBytes(message.data.byteLength, 'int32le'), ct);
            await this._stream.write(message.data, ct);
        }

        public async read(ct: CancellationToken): Promise<Message> {
            const type: Wire.Message.Type = BitConverter.getNumber(await Default.readFully(this._stream, 1, ct), 'uint8');
            const length = BitConverter.getNumber(await Default.readFully(this._stream, 4, ct), 'int32le');
            const data = await Default.readFully(this._stream, length, ct);

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
