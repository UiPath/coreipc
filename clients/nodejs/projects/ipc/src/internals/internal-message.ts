import { IChannelReader } from './channel-reader';
import { CancellationToken } from '@uipath/ipc-helpers';
import { IChannelWriter } from './channel-writer';

/* @internal */
export class InternalMessage {
    private static getMessageType(value: number): MessageType {
        switch (value) {
            case 0: return MessageType.Request;
            case 1: return MessageType.Response;
            default: throw new Error('Not supported');
        }
    }
    private static createMessage(messageType: MessageType, json: string): InternalMessage {
        const source = JSON.parse(json);
        switch (messageType) {
            case MessageType.Request:
                const request = new InternalRequestMessage(
                    source.TimeoutInSeconds,
                    source.MethodName,
                    source.Parameters
                );
                request.Id = source.Id;

                return request;
            case MessageType.Response:
                const response = new InternalResponseMessage(
                    source.Data,
                    source.Error
                );
                response.RequestId = source.RequestId;
                return response;
            default:
                throw new Error('Not supported');
        }
    }
    // tslint:disable-next-line: member-ordering
    public static async readAsync(reader: IChannelReader, cancellationToken: CancellationToken): Promise<InternalMessage> {
        const header = Buffer.alloc(5);
        await reader.readBufferAsync(header, cancellationToken);

        const messageType = InternalMessage.getMessageType(header.readUInt8(0));
        const cbPayload = header.readInt32LE(1);

        const payload = Buffer.alloc(cbPayload);
        await reader.readBufferAsync(payload, cancellationToken);

        const json = payload.toString('utf8');

        const message = InternalMessage.createMessage(messageType, json);

        return message;
    }

    public writeWithEnvelopeAsync(writer: IChannelWriter, cancellationToken: CancellationToken): Promise<void> {
        const json = JSON.stringify(this);
        const payload = Buffer.from(json);
        const withEnvelope = Buffer.alloc(5 + payload.length);
        // tslint:disable-next-line: no-angle-bracket-type-assertion
        withEnvelope.writeUInt8(<number> this.getMessageType(), 0);
        withEnvelope.writeInt32LE(payload.length, 1);
        payload.copy(withEnvelope, 5, 0);
        return writer.writeAsync(withEnvelope, cancellationToken);
    }

    public getMessageType(): MessageType { throw null; }
}

/* @internal */
export enum MessageType {
    Request,
    Response
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class InternalRequestMessage extends InternalMessage {
    // tslint:disable-next-line: variable-name
    public Id: string | null = null;

    // tslint:disable-next-line: variable-name
    constructor(public readonly TimeoutInSeconds: number, public readonly MethodName: string, public readonly Parameters: string[]) {
        super();
    }

    public getMessageType(): MessageType { return MessageType.Request; }
    public toString(): string { return JSON.stringify(this); }
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class InternalResponseMessage extends InternalMessage {
    // tslint:disable-next-line: variable-name
    public RequestId: string = '';

    // tslint:disable-next-line: variable-name
    constructor(public readonly Data: string | null, public readonly Error: IResponseError | null) {
        super();
    }

    public getMessageType(): MessageType { return MessageType.Response; }
    public toString(): string { return JSON.stringify(this); }
}

export interface IResponseError {
    readonly Message: string;
    readonly StackTrace: string;
    readonly Type: string;
    readonly InnerError: IResponseError | null;
}
