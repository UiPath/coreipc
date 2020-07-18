import { CancellationToken, TimeSpan } from '@foundation';

export type ConnectHelper = (tryConnect: () => Promise<boolean>, pipeName: string, timeout: TimeSpan, ct: CancellationToken) => Promise<void>;

/* @internal */
export const defaultConnectHelper: ConnectHelper = async (
    tryConnect: () => Promise<boolean>,
    _pipeName: string,
    _timeout: TimeSpan,
    _ct: CancellationToken,
): Promise<void> => {
    await tryConnect();
};
