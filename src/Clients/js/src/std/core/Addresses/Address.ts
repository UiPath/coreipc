import { CancellationToken, Socket, TimeSpan } from '../../bcl';
import { ConnectHelper } from '..';

export abstract class Address {
    /* @internal */
    public abstract get key(): string;

    /* @internal */
    public abstract connect<TSelf extends Address>(
        this: TSelf,
        helper: ConnectHelper<TSelf>,
        timeout: TimeSpan,
        ct: CancellationToken,
    ): Promise<Socket>;
}
