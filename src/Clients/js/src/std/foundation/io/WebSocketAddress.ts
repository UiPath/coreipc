import {
    TimeSpan,
    CancellationToken,
    IAddress,
    Socket,
    ClientWebSocket,
    WebSocketLikeCtor,
} from '@foundation';

export class WebSocketAddress implements IAddress {
    constructor(
        public readonly url: string,
        public readonly ctor?: WebSocketLikeCtor,
    ) { }

    public get key(): string { return `websocket/${this.url}`; }

    public async connectAsync(timeout: TimeSpan, ct: CancellationToken): Promise<Socket> {
        return await ClientWebSocket.connect(this, timeout, ct);
    }
}
