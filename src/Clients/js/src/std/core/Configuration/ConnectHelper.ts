import { Address } from '..';
import { ConnectContext } from '.';

export type ConnectHelper<TAddress extends Address> = (
    context: ConnectContext<TAddress>,
) => Promise<void>;

/* @internal */
export const defaultConnectHelper: ConnectHelper<Address> = async (
    context: ConnectContext<Address>,
): Promise<void> => {
    await context.tryConnect();
};
