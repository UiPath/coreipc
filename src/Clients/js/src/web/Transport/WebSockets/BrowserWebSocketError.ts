import { CoreIpcError } from '../../../std';
import { BrowserWebSocketLike } from './BrowserWebSocketLike';

export class BrowserWebSocketError extends CoreIpcError {}

export module BrowserWebSocketError {
    export class ConnectFailure extends BrowserWebSocketError {
        /* @internal */
        constructor(public readonly socket: BrowserWebSocketLike, message?: string) {
            super(message ?? ConnectFailure.defaultMessage);
        }

        private static readonly defaultMessage =
            'Received an error while awaiting for a WebSocket to connect.';
    }
}
