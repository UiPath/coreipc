import { CancellationToken, TimeSpan } from '../..';
import { Address } from '..';

export interface ConnectContext<TAddress extends Address> {
    readonly address: TAddress;
    readonly timeout: TimeSpan;
    readonly ct: CancellationToken;
    readonly tryConnect: () => Promise<boolean>;
}
