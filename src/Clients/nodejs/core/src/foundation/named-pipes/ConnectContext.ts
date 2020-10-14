import { CancellationToken, TimeSpan } from '..';

export interface ConnectContext {
    readonly pipeName: string;
    readonly timeout: TimeSpan;
    readonly ct: CancellationToken;
    readonly tryConnect: () => Promise<boolean>;
}
