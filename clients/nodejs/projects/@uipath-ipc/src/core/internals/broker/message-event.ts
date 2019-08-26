import * as WireMessage from './wire-message';
import { Emitter } from './emitter';

/* @internal */
export class MessageEvent {
    constructor(
        public readonly sender: Emitter,
        public readonly message: WireMessage.Request | WireMessage.Response
    ) { }
}
