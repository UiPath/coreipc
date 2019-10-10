import * as WireMessage from './wire-message';
import { StreamWrapper } from './stream-wrapper';

/* @internal */
export class MessageEvent {
    constructor(
        public readonly sender: StreamWrapper,
        public readonly message: WireMessage.Request | WireMessage.Response
    ) { }
}
