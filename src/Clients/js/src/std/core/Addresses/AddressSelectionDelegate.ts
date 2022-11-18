import { Address } from '.';
import { PublicCtor } from '../..';

export interface AddressSelectionDelegate<TAddressFactory, TAddress extends Address> {
    (addressBuilder: TAddressFactory): PublicCtor<TAddress>;
}
