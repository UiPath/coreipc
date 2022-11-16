import { TimeSpan } from '../..';
import { Address } from '..';
import { ConnectHelper } from '.';

export type ConfigBuilder<TAddress extends Address> = ConfigBuilder.SetConnectHelper<TAddress> &
    ConfigBuilder.SetRequestTimeout;

export module ConfigBuilder {
    export interface SetRequestTimeout {
        setRequestTimeout<T>(
            this: T,
            value: TimeSpan
        ): Omit<T, keyof SetRequestTimeout>;

        setRequestTimeout<T>(
            this: T,
            milliseconds: number
        ): Omit<T, keyof SetRequestTimeout>;
    }

    export interface SetConnectHelper<TAddress extends Address> {
        setConnectHelper<T>(
            this: T,
            connectHelper: ConnectHelper<TAddress>
        ): Omit<T, keyof SetConnectHelper<TAddress>>;
    }
}
