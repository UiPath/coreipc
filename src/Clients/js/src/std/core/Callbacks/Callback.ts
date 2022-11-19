import { PublicCtor } from '../..';
import { Address, AddressBuilder, AddressSelectionDelegate } from '../Addresses';

export interface Callback<TAddressBuilder extends AddressBuilder> {
    forAddress<TAddress extends Address>(
        configure: AddressSelectionDelegate<TAddressBuilder, TAddress>,
    ): CallbackForAddress;
}

export interface CallbackForAddress {
    forService<TService>(
        callbackService: PublicCtor<TService>,
    ): CallbackForAddressService<TService>;

    forService<TService>(callbackEndpointName: string): CallbackForAddressService<TService>;
}

export interface CallbackForAddressService<TService> {
    is(instance: TService): void;
}
