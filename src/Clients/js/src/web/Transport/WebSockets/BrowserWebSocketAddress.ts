import {
    TimeSpan,
    CancellationToken,
    Socket,
    ConnectHelper,
    Address,
} from '../../../std';
import { BrowserWebSocket } from './BrowserWebSocket';

export class BrowserWebSocketAddress extends Address {
    constructor(public readonly url: string) {
        super();
    }

    public override get key() {
        return `websocket:${this.url}`;
    }

    public override async connect<TSelf extends Address>(
        this: TSelf,
        helper: ConnectHelper<TSelf>,
        timeout: TimeSpan,
        ct: CancellationToken
    ): Promise<Socket> {
        return await BrowserWebSocket.connectWithHelper(
            helper as any,
            (this as unknown as BrowserWebSocketAddress).url,
            timeout,
            ct
        );
    }
}
