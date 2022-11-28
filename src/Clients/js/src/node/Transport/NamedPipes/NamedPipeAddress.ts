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

    public override async connect<TSelf extends Address>(
        this: TSelf,
        helper: ConnectHelper<TSelf>,
        timeout: TimeSpan,
        ct: CancellationToken,
    ): Promise<Socket> {
        return await NamedPipeSocket.connectWithHelper(
            helper as any,
            (this as unknown as NamedPipeAddress).name,
            timeout,
            ct,
        );
    }
}
