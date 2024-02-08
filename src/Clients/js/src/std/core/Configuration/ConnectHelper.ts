import { Address } from '..';
import { ConnectContext } from '.';

export type ConnectHelper<TAddress extends Address = any> = (context: ConnectContext<TAddress>) => Promise<void>;

/* @internal */
export const defaultConnectHelper: ConnectHelper<Address> = async (context: ConnectContext<Address>): Promise<void> => {
    await context.tryConnectAsync();
};
