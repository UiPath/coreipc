import { NodeWebSocket } from '.';
import { TimeSpan, CancellationToken, Socket, ConnectHelper, Address } from '../../../std';

export class NodeWebSocketAddress extends Address {
    constructor(public readonly url: string) {
        super();
    }

    public override get key() {
        return `websocket:${this.url}`;
    }

    public override async connect(helper: ConnectHelper, timeout: TimeSpan, ct: CancellationToken): Promise<Socket> {
        return await NodeWebSocket.connectWithHelper(helper, this.url, timeout, ct);
    }
}
