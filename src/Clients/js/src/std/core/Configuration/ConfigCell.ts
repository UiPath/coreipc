import { assertArgument, TimeSpan } from '../..';
import { Address } from '..';
import { ConfigBuilder, ConnectHelper } from '.';

/* @internal */
export class ConfigCell implements ConfigBuilder<Address> {
    public connectHelper: ConnectHelper<Address> | undefined;
    public requestTimeout: TimeSpan | undefined;

    setConnectHelper<T>(
        this: T,
        connectHelper: ConnectHelper<Address>,
    ): Omit<T, keyof ConfigBuilder.SetConnectHelper<Address>> {
        assertArgument({ connectHelper }, 'function');

        (this as ConfigCell).connectHelper = connectHelper;
        return this;
    }

    setRequestTimeout<T>(this: T, value: TimeSpan): Omit<T, keyof ConfigBuilder.SetRequestTimeout>;
    setRequestTimeout<T>(
        this: T,
        milliseconds: number,
    ): Omit<T, keyof ConfigBuilder.SetRequestTimeout>;
    setRequestTimeout(value: TimeSpan | number): any {
        let newValue: TimeSpan;

        switch (assertArgument({ value }, TimeSpan, 'number')) {
            case TimeSpan: {
                newValue = value as TimeSpan;
                break;
            }
            case 'number': {
                newValue = TimeSpan.fromMilliseconds(value as number);
                break;
            }
            default: {
                throw void 0;
            }
        }

        this.requestTimeout = newValue;
    }
}
