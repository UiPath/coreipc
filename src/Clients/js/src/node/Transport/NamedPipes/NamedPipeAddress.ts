import {
    Address,
    CancellationToken,
    ConnectHelper,
    Socket,
    TimeSpan,
} from '../../../std';
import { NamedPipeSocket } from './NamedPipeSocket';

export class NamedPipeAddress extends Address {
    constructor(public readonly name: string) {
        super();
    }

    public override get key() {
        return `namedpipe:${this.name}`;
    }

    public override async connect(helper: ConnectHelper, timeout: TimeSpan, ct: CancellationToken): Promise<Socket> {
        return await NamedPipeSocket.connectWithHelper(helper, this.name, timeout, ct);
    }
}
