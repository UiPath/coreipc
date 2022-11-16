import {
    Address,
    CancellationToken,
    ConnectHelper,
    Socket,
    TimeSpan,
} from '../std';

export class NamedPipeAddress extends Address {
    constructor(public readonly name: string) {
        super();
    }

    public override get key() {
        return `namedpipe:${this.name}`;
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
