import { CancellationToken, Socket, TimeSpan } from '../../bcl';
import { ConnectHelper } from '..';

export abstract class Address {
    /* @internal */
    public abstract get key(): string;

    /* @internal */
    public abstract connect(helper: ConnectHelper, timeout: TimeSpan, ct: CancellationToken): Promise<Socket>;
}
