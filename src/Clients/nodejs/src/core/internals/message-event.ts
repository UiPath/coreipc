import * as WireMessage from './wire-message';
import { IMessageStream } from './message-stream';

/* @internal */
export class MessageEvent {
    constructor(
        public readonly messageStream: IMessageStream,
        public readonly message: WireMessage.Request | WireMessage.Response
    ) { }
}
