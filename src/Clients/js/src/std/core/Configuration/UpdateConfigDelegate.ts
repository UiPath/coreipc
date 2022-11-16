import { Address } from '..';
import { ConfigBuilder } from '.';

export interface UpdateConfigDelegate<TAddress extends Address> {
    (builder: ConfigBuilder<TAddress>): void;
}
