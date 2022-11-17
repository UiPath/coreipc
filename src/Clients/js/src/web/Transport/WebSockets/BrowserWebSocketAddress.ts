import {
    TimeSpan,
    CancellationToken,
    Socket,
    ConnectHelper,
    Address,
} from '../../../std';

export class BrowserWebSocketAddress extends Address {
    constructor(public readonly url: string) {
        super();
    }

    public override get key() {
        return `websocket:${this.url}`;
    }

    public override connect<TSelf extends Address>(
        this: TSelf,
        helper: ConnectHelper<TSelf>,
        timeout: TimeSpan,
        ct: CancellationToken
    ): Promise<Socket> {
        throw new Error('Method not implemented.');
    }
}
