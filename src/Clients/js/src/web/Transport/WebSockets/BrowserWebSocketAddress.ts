import { TimeSpan, CancellationToken, Socket, ConnectHelper, Address } from '../../../std';
import { BrowserWebSocket } from './BrowserWebSocket';

export class BrowserWebSocketAddress extends Address {
    constructor(public readonly url: string) {
        super();
    }

    public override get key() {
        return `websocket:${this.url}`;
    }

    public override async connect(helper: ConnectHelper, timeout: TimeSpan, ct: CancellationToken): Promise<Socket> {
        return await BrowserWebSocket.connectWithHelper(helper, this.url, timeout, ct);
    }
}
