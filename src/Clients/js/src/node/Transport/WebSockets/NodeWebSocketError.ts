import { CoreIpcError } from '../../../std';
import { NodeWebSocketLike } from '.';

export class NodeWebSocketError extends CoreIpcError {}

export module NodeWebSocketError {
    export class ConnectFailure extends NodeWebSocketError {
        constructor(
            public readonly socket: NodeWebSocketLike,
            message?: string
        ) {
            super(message ?? ConnectFailure.defaultMessage);
        }

        private static readonly defaultMessage =
            'Received an error while awaiting for a WebSocket to connect.';
    }
}
