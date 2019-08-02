import { InternalRequestMessage, InternalResponseMessage, MessageType } from './internal-message';
import { IChannelWriter } from './channel-writer';
import { ArgumentNullError, CancellationToken } from '@uipath/ipc-helpers';

/* @internal */
export class CallbackContext {
    constructor(
        public readonly request: InternalRequestMessage,
        public readonly cancellationToken: CancellationToken,
        private readonly _writer: IChannelWriter
    ) {
        if (!request) {
            throw new ArgumentNullError('request');
        }
        if (!cancellationToken) {
            throw new ArgumentNullError('cancellationToken');
        }
        if (!_writer) {
            throw new ArgumentNullError('_writer');
        }
    }

    public async respondAsync(response: InternalResponseMessage, cancellationToken: CancellationToken): Promise<void> {
        cancellationToken = CancellationToken.combine(
            CancellationToken.defaultIfFalsy(cancellationToken),
            this.cancellationToken);

        if (!this.request.Id) {
            throw new Error('The request must have a non-null Id');
        }
        response.RequestId = this.request.Id;

        const bPayload = Buffer.from(JSON.stringify(response));
        const bMessage = Buffer.alloc(5 + bPayload.length);

        // tslint:disable-next-line: no-angle-bracket-type-assertion
        bMessage.writeUInt8(<number> MessageType.Response, 0);
        bMessage.writeInt32LE(bPayload.length, 1);
        bPayload.copy(bMessage, 5);

        await this._writer.writeAsync(bMessage, cancellationToken);
    }
}
