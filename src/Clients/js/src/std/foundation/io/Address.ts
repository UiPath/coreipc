import {
    CancellationToken,
    TimeSpan,
    Socket,
} from '@foundation';

export interface IAddress {
    connectAsync(timeout: TimeSpan, ct: CancellationToken): Promise<Socket>;

    readonly key: string;
}
