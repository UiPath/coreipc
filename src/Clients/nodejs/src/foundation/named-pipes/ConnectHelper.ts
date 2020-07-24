import { ConnectContext } from '.';

export type ConnectHelper = (context: ConnectContext) => Promise<void>;

/* @internal */
export const defaultConnectHelper: ConnectHelper =
    async (context: ConnectContext): Promise<void> => {
        await context.tryConnect();
    };
