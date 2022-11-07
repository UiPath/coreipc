import {
    CancellationToken,
    TimeSpan,

    IAddress,
} from '@foundation';

export interface ConnectContext {
    readonly address: IAddress;
    readonly timeout: TimeSpan;
    readonly ct: CancellationToken;
    readonly tryConnect: () => Promise<boolean>;
}
