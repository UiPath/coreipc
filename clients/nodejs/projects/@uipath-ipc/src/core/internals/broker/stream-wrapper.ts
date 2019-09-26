import * as WireMessage from './wire-message';
import { Observable, ReplaySubject } from 'rxjs';
import { PipeClientStream, CancellationToken } from '../../..';
import { SerializationPal } from './serialization-pal';
import { CancellationTokenSource } from '../../../foundation/tasks/cancellation-token-source';
import { IAsyncDisposable } from '../../../foundation/disposable';
import { MessageEvent } from './message-event';

/* @internal */
export class StreamWrapper implements IAsyncDisposable {
    private readonly _messages = new ReplaySubject<MessageEvent>();
    public get messages(): Observable<MessageEvent> { return this._messages; }
    private readonly _readIndefinitelyCts = new CancellationTokenSource();
    private readonly _readIndefinitelyPromise: Promise<void>;
    private readonly _headerBuffer = Buffer.alloc(5);

    private _isConnected = true;
    public get isConnected(): boolean { return this._isConnected; }

    constructor(public readonly stream: PipeClientStream) {
        this._readIndefinitelyPromise = this.readIndefinitelyAsync(this._readIndefinitelyCts.token);
    }
    private async readIndefinitelyAsync(token: CancellationToken): Promise<void> {
        try {
            while (!token.isCancellationRequested) {
                const message = await this.readMessageAsync(token);
                this._messages.next(new MessageEvent(this, message));
            }
        } catch (error) {
            this._isConnected = false;
        }
    }
    private async readMessageAsync(token: CancellationToken): Promise<WireMessage.Request | WireMessage.Response> {
        await this.stream.readAsync(this._headerBuffer, token);
        const type = this._headerBuffer.readUInt8(0) as WireMessage.Type;
        const length = this._headerBuffer.readInt32LE(1);

        const bytes = Buffer.alloc(length);
        await this.stream.readAsync(bytes, token);

        const json = bytes.toString('utf8');
        const message = SerializationPal.fromJson(json, type);
        return message;
    }
    public async disposeAsync(): Promise<void> {
        await this.stream.disposeAsync();

        this._messages.complete();

        this._readIndefinitelyCts.cancel(false);
        try {

            await this._readIndefinitelyPromise;
        } catch (error) {
            /* */
        }
    }
}
